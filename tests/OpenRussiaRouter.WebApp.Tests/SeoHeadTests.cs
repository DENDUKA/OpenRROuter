using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using BillingProto = OpenRussiaRouter.Contracts.Grpc.Billing.V1;
using CollectorGrpc = OpenRussiaRouter.Contracts.Grpc.Collector.V1.Collector;
using CollectorProto = OpenRussiaRouter.Contracts.Grpc.Collector.V1;
using ContentGrpc = OpenRussiaRouter.Contracts.Grpc.Content.V1.ContentService;
using ContentProto = OpenRussiaRouter.Contracts.Grpc.Content.V1;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class SeoHeadTests
{
    [Fact]
    public async Task Get_Root_EmitsCanonicalDescriptionAndOgTags()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("<link rel=\"canonical\" href=\"https://example.com/\" />");
        body.Should().Contain("<meta name=\"description\" content=\"");
        body.Should().Contain("единый OpenAI-совместимый API для работы с AI-моделями");
        body.Should().Contain("property=\"og:site_name\" content=\"OpenRussiaRouter\"");
        body.Should().Contain("property=\"og:type\" content=\"website\"");
        body.Should().Contain("property=\"og:title\"");
        body.Should().Contain("property=\"og:description\"");
        body.Should().Contain("property=\"og:url\" content=\"https://example.com/\"");
        body.Should().Contain("property=\"og:image\" content=\"https://example.com/og-image.png\"");
        body.Should().Contain("name=\"twitter:card\" content=\"summary_large_image\"");
        body.Should().Contain("name=\"twitter:image\" content=\"https://example.com/og-image.png\"");
        body.Should().NotContain("name=\"robots\"");
    }

    [Fact]
    public async Task Get_ModelsWithQuery_CanonicalStripsQuery()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/models?category=text");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("<link rel=\"canonical\" href=\"https://example.com/models\" />");
        body.Should().NotContain("canonical\" href=\"https://example.com/models?");
        body.Should().NotContain("name=\"robots\"");
    }

    [Fact]
    public async Task Get_ModelDetails_CanonicalMatchesPath()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/models/xiaomi/mimo-v2.5-pro");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("<link rel=\"canonical\" href=\"https://example.com/models/xiaomi/mimo-v2.5-pro\" />");
        body.Should().NotContain("name=\"robots\"");
    }

    [Fact]
    public async Task Get_ToolsWithQuery_CanonicalStripsQuery()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/tools?category=ide");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("<link rel=\"canonical\" href=\"https://example.com/tools\" />");
        body.Should().NotContain("canonical\" href=\"https://example.com/tools?");
        body.Should().NotContain("name=\"robots\"");
    }

    [Fact]
    public async Task Get_Login_EmitsNoIndexAndNoCanonical()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/login");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("<meta name=\"robots\" content=\"noindex,follow\" />");
        body.Should().NotContain("<link rel=\"canonical\"");
    }

    [Fact]
    public async Task Get_Signup_EmitsNoIndex()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/signup");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("<meta name=\"robots\" content=\"noindex,follow\" />");
    }

    [Fact]
    public async Task Get_IndexablePublicPages_AllEmitUniqueMetaDescriptions()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var urls = new[]
        {
            "/", "/models", "/models/xiaomi/mimo-v2.5-pro", "/providers", "/providers/xiaomi",
            "/tools", "/tools/cline", "/docs", "/support", "/legal/privacy", "/legal/offer"
        };

        var descriptions = new List<string>();
        foreach (var url in urls)
        {
            using var response = await client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"page {url} should be reachable");

            var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            var match = Regex.Match(body, "<meta name=\"description\" content=\"([^\"]*)\"");
            match.Success.Should().BeTrue($"page {url} must emit a meta description");

            var description = match.Groups[1].Value;
            description.Should().NotBeNullOrWhiteSpace($"page {url} must have a non-empty meta description");
            descriptions.Add(description);
        }

        descriptions.Should().OnlyHaveUniqueItems("indexable pages must not share meta descriptions");
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var billing = new FakeBillingClient();
        var content = new FakeContentClient();
        var collector = new FakeCollectorClient();

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PublicApiBaseUrl"] = "https://example.com",
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<BillingGrpc.BillingServiceClient>();
                services.AddSingleton<BillingGrpc.BillingServiceClient>(billing);
                services.RemoveAll<ContentGrpc.ContentServiceClient>();
                services.AddSingleton<ContentGrpc.ContentServiceClient>(content);
                services.RemoveAll<CollectorGrpc.CollectorClient>();
                services.AddSingleton<CollectorGrpc.CollectorClient>(collector);
            });
        });
    }

    private sealed class FakeBillingClient : BillingGrpc.BillingServiceClient
    {
        public override AsyncUnaryCall<BillingProto.ListPublicModelsResponse> ListPublicModelsAsync(
            BillingProto.ListPublicModelsRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var model = new BillingProto.PublicModel
            {
                Id = "xiaomi/mimo-v2.5-pro",
                DisplayName = "MiMo V2.5 Pro",
                UpstreamModelId = "mimo-v2.5-pro",
                InputPricePer1MUsd = "1",
                OutputPricePer1MUsd = "2",
                CachePricePer1MUsd = "0",
                Provider = "Xiaomi",
                ProviderId = "xiaomi",
            };
            var response = new BillingProto.ListPublicModelsResponse();
            response.Models.Add(model);
            return UnaryCall(response);
        }

        public override AsyncUnaryCall<BillingProto.ListProvidersResponse> ListModelProvidersAsync(
            BillingProto.ListProvidersRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var response = new BillingProto.ListProvidersResponse();
            response.Providers.Add(new BillingProto.ModelProvider
            {
                Id = "xiaomi",
                DisplayName = "Xiaomi",
            });
            return UnaryCall(response);
        }

        public override AsyncUnaryCall<BillingProto.GetModelResponse> GetModelAsync(
            BillingProto.GetModelRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var found = request.ModelId == "xiaomi/mimo-v2.5-pro";
            var response = new BillingProto.GetModelResponse { Found = found };
            if (found)
            {
                response.Model = new BillingProto.ModelDetail
                {
                    Id = request.ModelId,
                    DisplayName = "MiMo V2.5 Pro",
                    UpstreamModelId = "mimo-v2.5-pro",
                    Provider = "Xiaomi",
                    ProviderId = "xiaomi",
                };
            }

            return UnaryCall(response);
        }

        public override AsyncUnaryCall<BillingProto.GetProviderResponse> GetModelProviderAsync(
            BillingProto.GetProviderRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var found = request.ProviderId == "xiaomi";
            var response = new BillingProto.GetProviderResponse { Found = found };
            if (found)
            {
                response.Provider = new BillingProto.ModelProvider
                {
                    Id = request.ProviderId,
                    DisplayName = "Xiaomi",
                };
            }

            return UnaryCall(response);
        }

        public override AsyncUnaryCall<BillingProto.GetPlatformSettingResponse> GetPlatformSettingAsync(
            BillingProto.GetPlatformSettingRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return UnaryCall(new BillingProto.GetPlatformSettingResponse());
        }

        public override AsyncUnaryCall<BillingProto.GetPromotionSettingsResponse> GetPromotionSettingsAsync(
            BillingProto.GetPromotionSettingsRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return UnaryCall(new BillingProto.GetPromotionSettingsResponse());
        }

        public override AsyncUnaryCall<BillingProto.ListTagsResponse> ListTagsAsync(
            BillingProto.ListTagsRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return UnaryCall(new BillingProto.ListTagsResponse());
        }
    }

    private sealed class FakeContentClient : ContentGrpc.ContentServiceClient
    {
        public override AsyncUnaryCall<ContentProto.ListToolsResponse> ListToolsAsync(
            ContentProto.ListToolsRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var response = new ContentProto.ListToolsResponse();
            response.Tools.Add(new ContentProto.ToolSummary
            {
                Id = "cline",
                DisplayName = "Cline",
                CategoryId = "ide",
                IsPublished = true,
            });
            return UnaryCall(response);
        }

        public override AsyncUnaryCall<ContentProto.GetToolResponse> GetToolAsync(
            ContentProto.GetToolRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var found = request.ToolId == "cline";
            var response = new ContentProto.GetToolResponse { Found = found };
            if (found)
            {
                response.Tool = new ContentProto.Tool
                {
                    Id = request.ToolId,
                    DisplayName = "Cline",
                    IsPublished = true,
                };
            }

            return UnaryCall(response);
        }

        public override AsyncUnaryCall<ContentProto.ListToolCategoriesResponse> ListToolCategoriesAsync(
            ContentProto.ListToolCategoriesRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var response = new ContentProto.ListToolCategoriesResponse();
            response.Categories.Add(new ContentProto.ToolCategory
            {
                Id = "ide",
                NameRu = "IDE и редакторы",
                NameEn = "IDEs & editors",
                SortOrder = 100,
            });
            return UnaryCall(response);
        }
    }

    private sealed class FakeCollectorClient : CollectorGrpc.CollectorClient
    {
        public override AsyncUnaryCall<CollectorProto.ListNodeModelCatalogResponse> ListNodeModelCatalogAsync(
            CollectorProto.ListNodeModelCatalogRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return UnaryCall(new CollectorProto.ListNodeModelCatalogResponse());
        }
    }

    private static AsyncUnaryCall<TResponse> UnaryCall<TResponse>(TResponse response)
        where TResponse : class => new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
}
