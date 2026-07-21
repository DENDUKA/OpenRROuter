using FluentAssertions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.ContentService.Application.Exceptions;
using OpenRussiaRouter.ContentService.Application.Options;
using OpenRussiaRouter.ContentService.Application.Services;
using OpenRussiaRouter.ContentService.Domain.Entities;
using OpenRussiaRouter.ContentService.Infrastructure.Repository;

namespace OpenRussiaRouter.ContentService.Tests.Application;

public class ContentApplicationServiceTests
{
    /// <summary>Smallest valid PNG header — enough for the magic-byte check.</summary>
    private static byte[] PngBytes(int totalLength = 32)
    {
        var bytes = new byte[totalLength];
        ReadOnlySpan<byte> header = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        header.CopyTo(bytes);
        return bytes;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (ContentApplicationService Service, InMemoryContentRepository Repo, FixedTimeProvider Clock)
        CreateService(ContentLimitsOptions? limits = null)
    {
        var repo = new InMemoryContentRepository();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new ContentApplicationService(
            repo,
            Options.Create(limits ?? new ContentLimitsOptions()),
            clock);
        return (service, repo, clock);
    }

    private static Tool NewTool(string id = "cursor", string name = "Cursor") => new(id, name);

    [Theory]
    [InlineData("Cursor")]      // uppercase is normalized, not rejected
    [InlineData("open-webui")]
    [InlineData("vendor/tool")]
    public async Task UpsertTool_AcceptsValidSlugs(string id)
    {
        var (service, _, _) = CreateService();

        var stored = await service.UpsertToolAsync(NewTool(id), LogoUpdate.Keep, null, null);

        stored.Should().Be(id.ToLowerInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData("../etc/passwd")]
    [InlineData("-leading-dash")]
    [InlineData("has space")]
    public async Task UpsertTool_InvalidId_Throws(string id)
    {
        var (service, _, _) = CreateService();

        var act = () => service.UpsertToolAsync(NewTool(id), LogoUpdate.Keep, null, null);

        (await act.Should().ThrowAsync<ContentValidationException>())
            .Which.ErrorCode.Should().Be(ContentErrorCodes.InvalidId);
    }

    [Fact]
    public async Task UpsertTool_IdLongerThanLimit_Throws()
    {
        var (service, _, _) = CreateService();
        var id = new string('a', 51);

        var act = () => service.UpsertToolAsync(NewTool(id), LogoUpdate.Keep, null, null);

        (await act.Should().ThrowAsync<ContentValidationException>())
            .Which.ErrorCode.Should().Be(ContentErrorCodes.InvalidId);
    }

    [Fact]
    public async Task UpsertTool_MarkdownTooLong_Throws()
    {
        var (service, _, _) = CreateService();
        var tool = NewTool() with { SetupRu = new string('x', 65_537) };

        var act = () => service.UpsertToolAsync(tool, LogoUpdate.Keep, null, null);

        (await act.Should().ThrowAsync<ContentValidationException>())
            .Which.ErrorCode.Should().Be(ContentErrorCodes.TextTooLong);
    }

    [Fact]
    public async Task UpsertTool_NonHttpWebsite_Throws()
    {
        var (service, _, _) = CreateService();
        var tool = NewTool() with { WebsiteUrl = "javascript:alert(1)" };

        var act = () => service.UpsertToolAsync(tool, LogoUpdate.Keep, null, null);

        (await act.Should().ThrowAsync<ContentValidationException>())
            .Which.ErrorCode.Should().Be(ContentErrorCodes.InvalidUrl);
    }

    [Fact]
    public async Task UpsertTool_UnknownCategory_Throws()
    {
        var (service, _, _) = CreateService();
        var tool = NewTool() with { CategoryId = "nope" };

        var act = () => service.UpsertToolAsync(tool, LogoUpdate.Keep, null, null);

        (await act.Should().ThrowAsync<ContentValidationException>())
            .Which.ErrorCode.Should().Be(ContentErrorCodes.UnknownCategory);
    }

    [Fact]
    public async Task UpsertTool_KnownCategory_Succeeds()
    {
        var (service, _, _) = CreateService();
        await service.UpsertToolCategoryAsync(new ToolCategory("ide", "IDE", "IDE", 10));

        await service.UpsertToolAsync(NewTool() with { CategoryId = "ide" }, LogoUpdate.Keep, null, null);

        var stored = await service.GetToolAsync("cursor");
        stored!.CategoryId.Should().Be("ide");
    }

    [Fact]
    public async Task UpsertTool_PreservesCreatedAt_AndAdvancesUpdatedAt()
    {
        var (service, _, clock) = CreateService();
        await service.UpsertToolAsync(NewTool(), LogoUpdate.Keep, null, null);
        var created = (await service.GetToolAsync("cursor"))!.CreatedAtUtc;

        clock.Now = clock.Now.AddHours(3);
        await service.UpsertToolAsync(NewTool(name: "Cursor IDE"), LogoUpdate.Keep, null, null);

        var updated = await service.GetToolAsync("cursor");
        updated!.CreatedAtUtc.Should().Be(created);
        updated.UpdatedAtUtc.Should().Be(clock.Now);
        updated.DisplayName.Should().Be("Cursor IDE");
    }

    [Fact]
    public async Task UpsertTool_LogoKeep_LeavesExistingLogoIntact()
    {
        var (service, _, _) = CreateService();
        await service.UpsertToolAsync(NewTool(), LogoUpdate.Replace, PngBytes(), "image/png");

        await service.UpsertToolAsync(NewTool(name: "Renamed"), LogoUpdate.Keep, null, null);

        (await service.GetToolLogoAsync("cursor")).Should().NotBeNull();
    }

    [Fact]
    public async Task UpsertTool_LogoClear_RemovesLogo()
    {
        var (service, _, _) = CreateService();
        await service.UpsertToolAsync(NewTool(), LogoUpdate.Replace, PngBytes(), "image/png");

        await service.UpsertToolAsync(NewTool(), LogoUpdate.Clear, null, null);

        (await service.GetToolLogoAsync("cursor")).Should().BeNull();
    }

    [Fact]
    public async Task UpsertTool_LogoTypeMismatchingBytes_Throws()
    {
        var (service, _, _) = CreateService();

        // Declares PNG but the bytes are not a PNG.
        var act = () => service.UpsertToolAsync(
            NewTool(), LogoUpdate.Replace, "not an image"u8.ToArray(), "image/png");

        (await act.Should().ThrowAsync<ContentValidationException>())
            .Which.ErrorCode.Should().Be(ContentErrorCodes.LogoTypeNotAllowed);
    }

    [Fact]
    public async Task UpsertTool_LogoTooLarge_Throws()
    {
        var (service, _, _) = CreateService();

        var act = () => service.UpsertToolAsync(
            NewTool(), LogoUpdate.Replace, PngBytes(256 * 1024 + 1), "image/png");

        (await act.Should().ThrowAsync<ContentValidationException>())
            .Which.ErrorCode.Should().Be(ContentErrorCodes.LogoTooLarge);
    }

    [Fact]
    public async Task UpsertTool_NormalizesTags()
    {
        var (service, _, _) = CreateService();
        var tool = NewTool() with { Tags = ["  IDE ", "ide", "", "Free"] };

        await service.UpsertToolAsync(tool, LogoUpdate.Keep, null, null);

        var stored = await service.GetToolAsync("cursor");
        stored!.Tags.Should().BeEquivalentTo(["ide", "free"]);
    }

    [Fact]
    public async Task GetPublishedTool_DraftIsInvisible()
    {
        var (service, _, _) = CreateService();
        await service.UpsertToolAsync(NewTool(), LogoUpdate.Keep, null, null);

        (await service.GetPublishedToolAsync("cursor")).Should().BeNull();
        (await service.GetToolAsync("cursor")).Should().NotBeNull();
    }

    [Fact]
    public async Task ListPublishedTools_ExcludesDraftsAndSortsBySortOrderDesc()
    {
        var (service, _, _) = CreateService();
        await service.UpsertToolAsync(
            NewTool("low", "Low") with { IsPublished = true, SortOrder = 1 }, LogoUpdate.Keep, null, null);
        await service.UpsertToolAsync(
            NewTool("high", "High") with { IsPublished = true, SortOrder = 99 }, LogoUpdate.Keep, null, null);
        await service.UpsertToolAsync(NewTool("draft", "Draft"), LogoUpdate.Keep, null, null);

        var published = await service.ListPublishedToolsAsync(categoryId: null);

        published.Select(t => t.Id).Should().Equal("high", "low");
    }

    [Fact]
    public async Task SetToolPublished_MissingTool_Throws()
    {
        var (service, _, _) = CreateService();

        var act = () => service.SetToolPublishedAsync("ghost", true);

        (await act.Should().ThrowAsync<ContentValidationException>())
            .Which.ErrorCode.Should().Be(ContentErrorCodes.ToolNotFound);
    }

    // --- images ------------------------------------------------------------------------

    [Fact]
    public async Task AddToolImage_UnknownTool_Throws()
    {
        var (service, _, _) = CreateService();

        var act = () => service.AddToolImageAsync("ghost", "a.png", PngBytes(), null);

        (await act.Should().ThrowAsync<ContentValidationException>())
            .Which.ErrorCode.Should().Be(ContentErrorCodes.ToolNotFound);
    }

    [Fact]
    public async Task AddToolImage_StoresDetectedContentType_NotTheFileName()
    {
        var (service, _, _) = CreateService();
        await service.UpsertToolAsync(NewTool(), LogoUpdate.Keep, null, null);

        var id = await service.AddToolImageAsync("cursor", "screenshot.jpg", PngBytes(), "alt");

        var image = await service.GetToolImageAsync(id);
        image!.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task AddToolImage_NonImageBytes_Throws()
    {
        var (service, _, _) = CreateService();
        await service.UpsertToolAsync(NewTool(), LogoUpdate.Keep, null, null);

        // An executable renamed to .png must not pass.
        var act = () => service.AddToolImageAsync("cursor", "payload.png", "MZ\0"u8.ToArray(), null);

        (await act.Should().ThrowAsync<ContentValidationException>())
            .Which.ErrorCode.Should().Be(ContentErrorCodes.ImageTypeNotAllowed);
    }

    [Fact]
    public async Task AddToolImage_TooLarge_Throws()
    {
        var (service, _, _) = CreateService();
        await service.UpsertToolAsync(NewTool(), LogoUpdate.Keep, null, null);

        var act = () => service.AddToolImageAsync("cursor", "big.png", PngBytes(1024 * 1024 + 1), null);

        (await act.Should().ThrowAsync<ContentValidationException>())
            .Which.ErrorCode.Should().Be(ContentErrorCodes.ImageTooLarge);
    }

    [Fact]
    public async Task AddToolImage_BeyondPerToolLimit_Throws()
    {
        var (service, _, _) = CreateService(new ContentLimitsOptions { MaxImagesPerTool = 2 });
        await service.UpsertToolAsync(NewTool(), LogoUpdate.Keep, null, null);
        await service.AddToolImageAsync("cursor", "a.png", PngBytes(), null);
        await service.AddToolImageAsync("cursor", "b.png", PngBytes(), null);

        var act = () => service.AddToolImageAsync("cursor", "c.png", PngBytes(), null);

        (await act.Should().ThrowAsync<ContentValidationException>())
            .Which.ErrorCode.Should().Be(ContentErrorCodes.ImageLimitReached);
    }

    [Fact]
    public async Task DeleteTool_AlsoDropsItsImages()
    {
        var (service, _, _) = CreateService();
        await service.UpsertToolAsync(NewTool(), LogoUpdate.Keep, null, null);
        var imageId = await service.AddToolImageAsync("cursor", "a.png", PngBytes(), null);

        await service.DeleteToolAsync("cursor");

        (await service.GetToolImageAsync(imageId)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteToolImage_Missing_Throws()
    {
        var (service, _, _) = CreateService();

        var act = () => service.DeleteToolImageAsync(Guid.NewGuid());

        (await act.Should().ThrowAsync<ContentValidationException>())
            .Which.ErrorCode.Should().Be(ContentErrorCodes.ImageNotFound);
    }

    // --- categories --------------------------------------------------------------------

    [Fact]
    public async Task DeleteToolCategory_LeavesToolsUncategorized()
    {
        var (service, _, _) = CreateService();
        await service.UpsertToolCategoryAsync(new ToolCategory("ide", "IDE", "IDE", 10));
        await service.UpsertToolAsync(NewTool() with { CategoryId = "ide" }, LogoUpdate.Keep, null, null);

        await service.DeleteToolCategoryAsync("ide");

        var tool = await service.GetToolAsync("cursor");
        tool.Should().NotBeNull();
        tool!.CategoryId.Should().BeNull();
    }

    [Fact]
    public async Task UpsertToolCategory_MissingName_Throws()
    {
        var (service, _, _) = CreateService();

        var act = () => service.UpsertToolCategoryAsync(new ToolCategory("ide", "IDE", "", 0));

        (await act.Should().ThrowAsync<ContentValidationException>())
            .Which.ErrorCode.Should().Be(ContentErrorCodes.InvalidDisplayName);
    }
}
