using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ContentService.Application.Services;
using OpenRussiaRouter.ContentService.Infrastructure.Auth;
using OpenRussiaRouter.Contracts.Grpc.Content.V1;
using ContentGrpc = OpenRussiaRouter.Contracts.Grpc.Content.V1;

namespace OpenRussiaRouter.ContentService.Tests.Application;

/// <summary>
/// Verifies the gRPC surface end to end over the in-memory repository: the public/admin split,
/// the s2s interceptor wiring, and the error_code convention (no RpcException on bad input).
/// </summary>
public sealed class GrpcContentServiceIntegrationTests
{
    private const string AdminToken = "test-webappadmin-token";

    private static byte[] PngBytes(int totalLength = 32)
    {
        var bytes = new byte[totalLength];
        ReadOnlySpan<byte> header = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        header.CopyTo(bytes);
        return bytes;
    }

    [Fact]
    public async Task PublicListTools_HidesDrafts_WhileAdminListSeesThem()
    {
        await using var factory = BuildFactory();
        var client = CreateClient(factory);

        await client.UpsertToolAsync(new UpsertToolRequest
        {
            Id = "cline",
            DisplayName = "Cline",
            IsPublished = false
        }, Admin());

        var publicList = await client.ListToolsAsync(new ListToolsRequest());
        var adminList = await client.ListToolsForAdminAsync(new ListToolsForAdminRequest(), Admin());

        publicList.Tools.Should().BeEmpty();
        adminList.Tools.Select(t => t.Id).Should().Equal("cline");
    }

    [Fact]
    public async Task GetTool_OnDraft_IsNotFound_ButAdminGetReturnsIt()
    {
        await using var factory = BuildFactory();
        var client = CreateClient(factory);

        await client.UpsertToolAsync(new UpsertToolRequest
        {
            Id = "cline",
            DisplayName = "Cline",
            SetupRu = "## Как подключить"
        }, Admin());

        var publicGet = await client.GetToolAsync(new GetToolRequest { ToolId = "cline" });
        var adminGet = await client.GetToolForAdminAsync(new GetToolRequest { ToolId = "cline" }, Admin());

        publicGet.Found.Should().BeFalse();
        adminGet.Found.Should().BeTrue();
        adminGet.Tool.SetupRu.Should().Be("## Как подключить");
    }

    [Fact]
    public async Task PublishedTool_BecomesVisible()
    {
        await using var factory = BuildFactory();
        var client = CreateClient(factory);
        await client.UpsertToolAsync(new UpsertToolRequest { Id = "cline", DisplayName = "Cline" }, Admin());

        var setResult = await client.SetToolPublishedAsync(
            new SetToolPublishedRequest { ToolId = "cline", IsPublished = true }, Admin());

        setResult.Updated.Should().BeTrue();
        (await client.GetToolAsync(new GetToolRequest { ToolId = "cline" })).Found.Should().BeTrue();
    }

    [Fact]
    public async Task UpsertTool_InvalidId_ReturnsErrorCode_NotRpcException()
    {
        await using var factory = BuildFactory();
        var client = CreateClient(factory);

        var response = await client.UpsertToolAsync(new UpsertToolRequest
        {
            Id = "../etc/passwd",
            DisplayName = "Bad"
        }, Admin());

        response.Upserted.Should().BeFalse();
        response.ErrorCode.Should().Be(ContentErrorCodes.InvalidId);
    }

    [Fact]
    public async Task UploadToolImage_RoundTripsBytes()
    {
        await using var factory = BuildFactory();
        var client = CreateClient(factory);
        await client.UpsertToolAsync(new UpsertToolRequest { Id = "cline", DisplayName = "Cline" }, Admin());
        var payload = PngBytes(128);

        var upload = await client.UploadToolImageAsync(new UploadToolImageRequest
        {
            ToolId = "cline",
            FileName = "screenshot.png",
            ContentType = "image/png",
            Bytes = ByteString.CopyFrom(payload)
        }, Admin());

        upload.Uploaded.Should().BeTrue();

        // The image endpoint is public: the markdown renders for anonymous visitors.
        var image = await client.GetToolImageAsync(new GetToolImageRequest { ImageId = upload.ImageId });
        image.Found.Should().BeTrue();
        image.Bytes.ToByteArray().Should().Equal(payload);
        image.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task GetToolImage_MalformedId_IsNotFound()
    {
        await using var factory = BuildFactory();
        var client = CreateClient(factory);

        var image = await client.GetToolImageAsync(new GetToolImageRequest { ImageId = "not-a-guid" });

        image.Found.Should().BeFalse();
    }

    [Theory]
    [InlineData("ListToolsForAdmin")]
    [InlineData("GetToolForAdmin")]
    [InlineData("UpsertTool")]
    [InlineData("DeleteTool")]
    [InlineData("SetToolPublished")]
    [InlineData("ListToolImages")]
    [InlineData("UploadToolImage")]
    [InlineData("DeleteToolImage")]
    [InlineData("UpsertToolCategory")]
    [InlineData("DeleteToolCategory")]
    public async Task AdminMethod_WithoutS2sToken_ReturnsUnauthenticated(string methodName)
    {
        await using var factory = BuildFactory();
        var client = CreateClient(factory);

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => InvokeAdminMethodAsync(client, methodName, default));

        ex.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task AdminMethod_WithCallerOutsideAllowlist_ReturnsPermissionDenied()
    {
        await using var factory = BuildFactory(opts =>
        {
            opts.Tokens["apigateway"] = "gateway-token";
            // apigateway authenticates, but UpsertTool only allows webappadmin.
        });
        var client = CreateClient(factory);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.UpsertToolAsync(
                new UpsertToolRequest { Id = "x", DisplayName = "X" },
                S2sHeaders("apigateway", "gateway-token")));

        ex.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task AdminMethod_WithMissingMethodPolicy_FailsClosed()
    {
        await using var factory = BuildFactory(opts => opts.MethodPolicies.Remove("UpsertTool"));
        var client = CreateClient(factory);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.UpsertToolAsync(new UpsertToolRequest { Id = "x", DisplayName = "X" }, Admin()));

        ex.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task PublicMethods_WithoutS2sToken_StillSucceed()
    {
        await using var factory = BuildFactory();
        var client = CreateClient(factory);

        var tools = await client.ListToolsAsync(new ListToolsRequest());
        var categories = await client.ListToolCategoriesAsync(new ListToolCategoriesRequest());
        var logo = await client.GetToolLogoAsync(new GetToolLogoRequest { ToolId = "missing" });

        tools.Should().NotBeNull();
        categories.Should().NotBeNull();
        logo.Found.Should().BeFalse();
    }

    private static WebApplicationFactory<Program> BuildFactory(Action<ServiceAuthOptions>? configureAuth = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SqlMigrations:Enabled"] = "false",
                    ["ConnectionStrings:ContentDb"] = ""
                }));
            // Replace ServiceAuthOptions authoritatively rather than relying on the service's
            // appsettings.json being copied into the test bin output.
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<ServiceAuthOptions>(opts =>
                {
                    opts.Tokens["webappadmin"] = AdminToken;
                    foreach (var method in ServiceAuthInterceptor.ProtectedMethods)
                    {
                        opts.MethodPolicies[method] = ["webappadmin"];
                    }

                    configureAuth?.Invoke(opts);
                });
            });
        });
    }

    private static ContentGrpc.ContentService.ContentServiceClient CreateClient(
        WebApplicationFactory<Program> factory)
    {
        var httpClient = factory.CreateDefaultClient();
        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
        return new ContentGrpc.ContentService.ContentServiceClient(channel);
    }

    private static CallOptions Admin() => S2sHeaders("webappadmin", AdminToken);

    private static CallOptions S2sHeaders(string caller, string token)
    {
        var headers = new Metadata
        {
            { ServiceAuthOptions.CallerMetadataKey, caller },
            { ServiceAuthOptions.TokenMetadataKey, token }
        };
        return new CallOptions(headers: headers);
    }

    private static async Task InvokeAdminMethodAsync(
        ContentGrpc.ContentService.ContentServiceClient client,
        string methodName,
        CallOptions options)
    {
        switch (methodName)
        {
            case "ListToolsForAdmin":
                await client.ListToolsForAdminAsync(new ListToolsForAdminRequest(), options);
                break;
            case "GetToolForAdmin":
                await client.GetToolForAdminAsync(new GetToolRequest { ToolId = "x" }, options);
                break;
            case "UpsertTool":
                await client.UpsertToolAsync(new UpsertToolRequest { Id = "x", DisplayName = "X" }, options);
                break;
            case "DeleteTool":
                await client.DeleteToolAsync(new DeleteToolRequest { ToolId = "x" }, options);
                break;
            case "SetToolPublished":
                await client.SetToolPublishedAsync(new SetToolPublishedRequest { ToolId = "x" }, options);
                break;
            case "ListToolImages":
                await client.ListToolImagesAsync(new ListToolImagesRequest { ToolId = "x" }, options);
                break;
            case "UploadToolImage":
                await client.UploadToolImageAsync(new UploadToolImageRequest { ToolId = "x" }, options);
                break;
            case "DeleteToolImage":
                await client.DeleteToolImageAsync(
                    new DeleteToolImageRequest { ImageId = Guid.NewGuid().ToString() }, options);
                break;
            case "UpsertToolCategory":
                await client.UpsertToolCategoryAsync(
                    new UpsertToolCategoryRequest { Id = "x", NameRu = "X", NameEn = "X" }, options);
                break;
            case "DeleteToolCategory":
                await client.DeleteToolCategoryAsync(new DeleteToolCategoryRequest { CategoryId = "x" }, options);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(methodName), methodName, "Unhandled admin method.");
        }
    }
}
