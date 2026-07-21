using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.WebApp.Pages;
using OpenRussiaRouter.WebApp.Services.Content;

namespace OpenRussiaRouter.WebApp.Tests;

public class ToolPagesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    /// <summary>Runs an assertion under a specific UI culture and restores the previous one.</summary>
    private static async Task WithCulture(string culture, Func<Task> body)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            await body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static ToolsModel CreateListModel(FakeContentProvider content)
    {
        var model = new ToolsModel(content, NullStringLocalizer<SharedResource>.Instance, NullLogger<ToolsModel>.Instance);
        model.PageContext = new PageContext { HttpContext = new DefaultHttpContext() };
        return model;
    }

    private static ToolDetailsModel CreateDetailsModel(FakeContentProvider content, string id)
    {
        var model = new ToolDetailsModel(
            content, NullStringLocalizer<SharedResource>.Instance, NullLogger<ToolDetailsModel>.Instance)
        {
            Id = id
        };
        model.PageContext = new PageContext { HttpContext = new DefaultHttpContext() };
        return model;
    }

    private static ToolSummaryInfo Summary(string id, string? taglineRu = "кратко", string? taglineEn = "short") =>
        new(id, $"Tool {id}", "ide", ["free"], taglineRu, taglineEn, null, false, true, 0, Now);

    [Fact]
    public async Task ToolsPage_UsesRussianTagline_UnderRussianCulture()
    {
        await WithCulture("ru", async () =>
        {
            var model = CreateListModel(new FakeContentProvider([Summary("cline")]));

            await model.OnGetAsync(category: null);

            model.Tools.Single().Tagline.Should().Be("кратко");
        });
    }

    [Fact]
    public async Task ToolsPage_FallsBackToEnglish_WhenRussianTaglineMissing()
    {
        await WithCulture("ru", async () =>
        {
            var model = CreateListModel(new FakeContentProvider([Summary("cline", taglineRu: null)]));

            await model.OnGetAsync(category: null);

            model.Tools.Single().Tagline.Should().Be("short");
        });
    }

    [Fact]
    public async Task ToolsPage_PassesCategoryFilterThrough()
    {
        var content = new FakeContentProvider([Summary("cline")]);
        var model = CreateListModel(content);

        await model.OnGetAsync(category: "ide");

        content.LastCategoryFilter.Should().Be("ide");
        model.SelectedCategoryId.Should().Be("ide");
    }

    [Fact]
    public async Task ToolsPage_BlankCategory_IsTreatedAsNoFilter()
    {
        var content = new FakeContentProvider([Summary("cline")]);
        var model = CreateListModel(content);

        await model.OnGetAsync(category: "   ");

        content.LastCategoryFilter.Should().BeNull();
        model.SelectedCategoryId.Should().BeNull();
    }

    [Fact]
    public async Task ToolsPage_ProviderFailure_SetsErrorInsteadOfThrowing()
    {
        var model = CreateListModel(new FakeContentProvider([], throwOnList: true));

        await model.OnGetAsync(category: null);

        model.ErrorMessage.Should().NotBeNull();
        model.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task ToolDetails_MissingTool_SetsNotFoundMessage()
    {
        var model = CreateDetailsModel(new FakeContentProvider([]), "ghost");

        await model.OnGetAsync();

        model.Tool.Should().BeNull();
        model.ErrorMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task ToolDetails_RendersBothMarkdownBlocks()
    {
        var tool = new ToolInfo(
            "cline",
            "Cline",
            SetupRu: "## Подключение\n\n1. Вставьте ключ",
            DescriptionRu: "**Агент** для VS Code");
        var model = CreateDetailsModel(new FakeContentProvider([], tool), "cline");

        await WithCulture("ru", async () =>
        {
            await model.OnGetAsync();

            model.DescriptionHtml.Should().Contain("<strong>");
            model.SetupHtml.Should().Contain("<h2");
            model.SetupHtml.Should().Contain("Вставьте ключ");
        });
    }

    [Fact]
    public async Task ToolDetails_StripsRemoteImagesFromMarkdown()
    {
        var tool = new ToolInfo(
            "cline",
            "Cline",
            SetupEn: "![tracker](https://evil.example/p.png)\n\n![ours](/tools/image/1)");
        var model = CreateDetailsModel(new FakeContentProvider([], tool), "cline");

        await WithCulture("en", async () =>
        {
            await model.OnGetAsync();

            model.SetupHtml.Should().NotContain("evil.example");
            model.SetupHtml.Should().Contain("/tools/image/1");
        });
    }

    [Fact]
    public async Task ToolDetails_ProviderFailure_SetsErrorInsteadOfThrowing()
    {
        var model = CreateDetailsModel(new FakeContentProvider([], throwOnList: true), "cline");

        await model.OnGetAsync();

        model.ErrorMessage.Should().NotBeNull();
    }

    private sealed class FakeContentProvider(
        IReadOnlyList<ToolSummaryInfo> tools,
        ToolInfo? tool = null,
        bool throwOnList = false)
        : ContentProvider(client: default!)
    {
        public string? LastCategoryFilter { get; private set; }

        public override Task<IReadOnlyList<ToolSummaryInfo>> ListToolsAsync(
            string? categoryId = null,
            CancellationToken cancellationToken = default)
        {
            if (throwOnList)
            {
                throw new InvalidOperationException("boom");
            }

            LastCategoryFilter = categoryId;
            return Task.FromResult(tools);
        }

        public override Task<ToolInfo?> GetToolAsync(string toolId, CancellationToken cancellationToken = default)
        {
            if (throwOnList)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.FromResult(tool);
        }

        public override Task<IReadOnlyList<ToolCategoryInfo>> ListToolCategoriesAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ToolCategoryInfo> categories = [new("ide", "IDE и редакторы", "IDEs & editors", 100)];
            return Task.FromResult(categories);
        }
    }
}
