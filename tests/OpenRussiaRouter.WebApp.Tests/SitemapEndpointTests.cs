using System.Net;
using System.Xml.Linq;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenRussiaRouter.WebApp.Api;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Content;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using BillingProto = OpenRussiaRouter.Contracts.Grpc.Billing.V1;
using CollectorGrpc = OpenRussiaRouter.Contracts.Grpc.Collector.V1.Collector;
using CollectorProto = OpenRussiaRouter.Contracts.Grpc.Collector.V1;
using ContentGrpc = OpenRussiaRouter.Contracts.Grpc.Content.V1.ContentService;
using ContentProto = OpenRussiaRouter.Contracts.Grpc.Content.V1;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class SitemapEndpointTests
{
    private static readonly DateTimeOffset Updated = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    // ── unit: Collect ───────────────────────────────────────────────────────

    [Fact]
    public void Collect_IncludesStaticIndexablePaths()
    {
        var entries = SitemapEndpoint.Collect("https://example.com", [], [], []);

        var paths = entries.Select(e => e.Url.Replace("https://example.com", "")).ToList();
        paths.Should().Contain("/");
        paths.Should().Contain("/models");
        paths.Should().Contain("/providers");
        paths.Should().Contain("/tools");
        paths.Should().Contain("/docs");
        paths.Should().Contain("/support");
        paths.Should().Contain("/legal/offer");
        paths.Should().Contain("/legal/privacy");
        // Никаких query-параметров и служебных разделов.
        entries.Select(e => e.Url).Should().NotContain(e => e.Contains('?'));
        entries.Select(e => e.Url).Should().NotContain(e => e.Contains("/admin"));
        entries.Select(e => e.Url).Should().NotContain(e => e.Contains("/workspace"));
    }

    [Fact]
    public void Collect_IncludesModelsWithSingleSlashId_AndSkipsUnroutableOnes()
    {
        var models = new[]
        {
            new ModelInfo("xiaomi/mimo-v2.5-pro", "MiMo V2.5 Pro", "mimo-v2.5-pro", "", 1m, 2m, 0m, true),
            new ModelInfo("ocg/deepseek-v4-flash", "DeepSeek V4 Flash", "deepseek-v4-flash", "", 1m, 2m, 0m, true),
            // Без слэша или с несколькими слэшами — рабочего двухсегментного URL нет.
            new ModelInfo("no-slash-model", "No Slash", "no-slash", "", 1m, 2m, 0m, true),
            new ModelInfo("a/b/c", "Multi Slash", "multi", "", 1m, 2m, 0m, true),
        };

        var entries = SitemapEndpoint.Collect("https://example.com", models, [], []);

        var urls = entries.Select(e => e.Url).ToList();
        urls.Should().Contain("https://example.com/models/xiaomi/mimo-v2.5-pro");
        urls.Should().Contain("https://example.com/models/ocg/deepseek-v4-flash");
        urls.Should().NotContain(e => e.Contains("no-slash", StringComparison.Ordinal));
        urls.Should().NotContain(e => e.EndsWith("a/b/c", StringComparison.Ordinal));
        urls.Should().NotContain(e => e.Contains("models/a/", StringComparison.Ordinal));
    }

    [Fact]
    public void Collect_IncludesOnlyProvidersWithActiveModels_WithLastMod()
    {
        var models = new[]
        {
            new ModelInfo("xiaomi/mimo-v2.5-pro", "MiMo", "mimo-v2.5-pro", "", 1m, 2m, 0m, true, ProviderId: "xiaomi"),
        };
        var providers = new[]
        {
            new ProviderInfo("xiaomi", "Xiaomi", UpdatedAtUtc: Updated),
            new ProviderInfo("empty", "Empty Provider"),
        };

        var entries = SitemapEndpoint.Collect("https://example.com", models, providers, []);

        var xiaomi = entries.Single(e => e.Url == "https://example.com/providers/xiaomi");
        xiaomi.LastModUtc.Should().Be(Updated);

        entries.Select(e => e.Url).Should().NotContain("https://example.com/providers/empty");
    }

    [Fact]
    public void Collect_IncludesOnlyPublishedTools_WithLastMod()
    {
        var tools = new[]
        {
            new ToolSummaryInfo("cline", "Cline", "ide", [], "кратко", "short", null, false, true, 0, Updated),
            new ToolSummaryInfo("draft", "Draft", "ide", [], "кратко", "short", null, false, false, 0, Updated),
        };

        var entries = SitemapEndpoint.Collect("https://example.com", [], [], tools);

        var cline = entries.Single(e => e.Url == "https://example.com/tools/cline");
        cline.LastModUtc.Should().Be(Updated);

        entries.Select(e => e.Url).Should().NotContain("https://example.com/tools/draft");
    }

    // ── unit: BuildXml ──────────────────────────────────────────────────────

    [Fact]
    public void BuildXml_ProducesValidSitemapDocument_WithLastModDateOnly()
    {
        var entries = new[]
        {
            new SitemapEndpoint.SitemapEntry("https://example.com/"),
            new SitemapEndpoint.SitemapEntry("https://example.com/tools/cline", Updated),
        };

        var xml = SitemapEndpoint.BuildXml(entries);

        xml.Should().StartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        xml.Should().Contain("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        xml.Should().Contain("<loc>https://example.com/</loc>");
        xml.Should().Contain("<loc>https://example.com/tools/cline</loc>");
        xml.Should().Contain("<lastmod>2026-08-06</lastmod>");

        var doc = XDocument.Parse(xml);
        doc.Root!.Name.LocalName.Should().Be("urlset");
        var urls = doc.Root.Elements().ToList();
        urls.Should().HaveCount(2);
        urls[0].Element(doc.Root.Name.Namespace + "lastmod").Should().BeNull();
        urls[1].Element(doc.Root.Name.Namespace + "lastmod")!.Value.Should().Be("2026-08-06");
    }

    // ── integration: GET /sitemap.xml ───────────────────────────────────────

    [Fact]
    public async Task Get_SitemapXml_ReturnsXmlWithStaticAndDynamicUrls()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/sitemap.xml");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("https://example.com/models/xiaomi/mimo-v2.5-pro");
        body.Should().Contain("https://example.com/providers/xiaomi");
        body.Should().Contain("https://example.com/tools/cline");
        body.Should().Contain("<lastmod>2026-08-06</lastmod>");
        body.Should().NotContain("providers/empty");
        body.Should().NotContain("tools/draft");

        // Критерий: каждый URL из sitemap реально отдаёт 200. Абсолютный origin (https://example.com)
        // в тестовом хосте недоступен — ходим по локальному пути к тому же приложению.
        var doc = XDocument.Parse(body);
        foreach (var loc in doc.Root!.Elements()
                     .Select(u => u.Element(doc.Root.Name.Namespace + "loc")!.Value))
        {
            var path = new Uri(loc).PathAndQuery;
            using var page = await client.GetAsync(path);
            page.StatusCode.Should().Be(HttpStatusCode.OK, $"sitemap URL {loc} should return 200");
        }
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

    // ── fakes ───────────────────────────────────────────────────────────────

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
                UpdatedAtUtc = "2026-08-06T12:00:00.0000000Z",
            });
            response.Providers.Add(new BillingProto.ModelProvider
            {
                Id = "empty",
                DisplayName = "Empty Provider",
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
                    UpdatedAtUtc = "2026-08-06T12:00:00.0000000Z",
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
                UpdatedAtUtc = "2026-08-06T12:00:00.0000000Z",
            });
            response.Tools.Add(new ContentProto.ToolSummary
            {
                Id = "draft",
                DisplayName = "Draft",
                IsPublished = false,
                UpdatedAtUtc = "2026-08-06T12:00:00.0000000Z",
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
                    UpdatedAtUtc = "2026-08-06T12:00:00.0000000Z",
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
