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
        var entries = SitemapEndpoint.Collect([], [], []);

        var paths = entries.Select(e => e.Path).ToList();
        paths.Should().Contain("/");
        paths.Should().Contain("/models");
        paths.Should().Contain("/providers");
        paths.Should().Contain("/tools");
        paths.Should().Contain("/support");
        paths.Should().Contain("/legal/offer");
        paths.Should().Contain("/legal/privacy");
        // Конечный URL документации — /docs/ (в проде его отдаёт контейнер VitePress);
        // /docs без слэша уходит редиректом на /docs/.
        paths.Should().Contain("/docs/");
        // Никаких query-параметров и служебных разделов.
        paths.Should().NotContain(e => e.Contains('?'));
        paths.Should().NotContain(e => e.Contains("/admin"));
        paths.Should().NotContain(e => e.Contains("/workspace"));
    }

    [Fact]
    public void Collect_MarksDocsAsNonLocalized_ButEverythingElseLocalized()
    {
        // /docs живёт в контейнере VitePress со своей двуязычной картой сайта; ветку /en/docs
        // это приложение отдаёт только редиректом, и в карту она попасть не должна.
        var entries = SitemapEndpoint.Collect([], [], []);

        entries.Single(e => e.Path == "/docs/").Localized.Should().BeFalse();
        entries.Where(e => e.Path != "/docs/").Should().OnlyContain(e => e.Localized);
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

        var entries = SitemapEndpoint.Collect(models, [], []);

        var paths = entries.Select(e => e.Path).ToList();
        paths.Should().Contain("/models/xiaomi/mimo-v2.5-pro");
        paths.Should().Contain("/models/ocg/deepseek-v4-flash");
        paths.Should().NotContain(e => e.Contains("no-slash", StringComparison.Ordinal));
        paths.Should().NotContain(e => e.EndsWith("a/b/c", StringComparison.Ordinal));
        paths.Should().NotContain(e => e.Contains("models/a/", StringComparison.Ordinal));
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

        var entries = SitemapEndpoint.Collect(models, providers, []);

        var xiaomi = entries.Single(e => e.Path == "/providers/xiaomi");
        xiaomi.LastModUtc.Should().Be(Updated);

        entries.Select(e => e.Path).Should().NotContain("/providers/empty");
    }

    [Fact]
    public void Collect_IncludesOnlyPublishedTools_WithLastMod()
    {
        var tools = new[]
        {
            new ToolSummaryInfo("cline", "Cline", "ide", [], "кратко", "short", null, false, true, 0, Updated),
            new ToolSummaryInfo("draft", "Draft", "ide", [], "кратко", "short", null, false, false, 0, Updated),
        };

        var entries = SitemapEndpoint.Collect([], [], tools);

        var cline = entries.Single(e => e.Path == "/tools/cline");
        cline.LastModUtc.Should().Be(Updated);

        entries.Select(e => e.Path).Should().NotContain("/tools/draft");
    }

    // ── unit: BuildXml ──────────────────────────────────────────────────────

    [Fact]
    public void BuildXml_ProducesValidSitemapDocument_WithLastModDateOnly()
    {
        var entries = new[]
        {
            new SitemapEndpoint.SitemapEntry("/", Localized: false),
            new SitemapEndpoint.SitemapEntry("/tools/cline", Updated, Localized: false),
        };

        var xml = SitemapEndpoint.BuildXml("https://example.com", entries);

        xml.Should().StartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        xml.Should().Contain("xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"");
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

    [Fact]
    public void BuildXml_LocalizedEntry_EmitsBothLocales_EachWithFullReciprocalAlternateSet()
    {
        var xml = SitemapEndpoint.BuildXml(
            "https://example.com",
            [new SitemapEndpoint.SitemapEntry("/models")]);

        var doc = XDocument.Parse(xml);
        XNamespace sm = "http://www.sitemaps.org/schemas/sitemap/0.9";
        XNamespace xhtml = "http://www.w3.org/1999/xhtml";

        var urls = doc.Root!.Elements(sm + "url").ToList();
        urls.Select(u => u.Element(sm + "loc")!.Value).Should().Equal(
            "https://example.com/ru/models",
            "https://example.com/en/models");

        // Каждая версия обязана перечислять все версии, включая саму себя, плюс x-default —
        // несимметричный набор поисковики отбрасывают целиком.
        foreach (var url in urls)
        {
            var links = url.Elements(xhtml + "link")
                .Select(l => (Lang: l.Attribute("hreflang")!.Value, Href: l.Attribute("href")!.Value))
                .ToList();

            links.Should().BeEquivalentTo(new[]
            {
                ("ru", "https://example.com/ru/models"),
                ("en", "https://example.com/en/models"),
                ("x-default", "https://example.com/en/models"),
            }.Select(t => (Lang: t.Item1, Href: t.Item2)));
            url.Elements(xhtml + "link").Should().OnlyContain(l => l.Attribute("rel")!.Value == "alternate");
        }
    }

    [Fact]
    public void BuildXml_HomePage_LocsArePrefixWithoutTrailingSlash()
    {
        // "/en/" и "/ru/" редиректят; карта сайта обязана перечислять ту форму, которая отдаёт 200.
        var xml = SitemapEndpoint.BuildXml(
            "https://example.com",
            [new SitemapEndpoint.SitemapEntry("/")]);

        xml.Should().Contain("<loc>https://example.com/ru</loc>");
        xml.Should().NotContain("<loc>https://example.com/ru/</loc>");
        xml.Should().Contain("<loc>https://example.com/en</loc>");
        xml.Should().NotContain("<loc>https://example.com/en/</loc>");
    }

    [Fact]
    public void BuildXml_NonLocalizedEntry_HasNoAlternateAnnotations()
    {
        // Sitemap публикует конечный URL документации — /docs/ (в проде его отдаёт VitePress
        // напрямую), чтобы URL из карты сразу отвечал 200 без redirect-цепочки.
        var xml = SitemapEndpoint.BuildXml(
            "https://example.com",
            [new SitemapEndpoint.SitemapEntry("/docs/", Localized: false)]);

        var doc = XDocument.Parse(xml);
        XNamespace sm = "http://www.sitemaps.org/schemas/sitemap/0.9";
        XNamespace xhtml = "http://www.w3.org/1999/xhtml";

        var urls = doc.Root!.Elements(sm + "url").ToList();
        urls.Should().ContainSingle();
        urls[0].Element(sm + "loc")!.Value.Should().Be("https://example.com/docs/");
        urls[0].Elements(xhtml + "link").Should().BeEmpty();
        xml.Should().NotContain("/en/docs");
        xml.Should().NotContain("/ru/docs");
    }

    // ── integration: GET /sitemap.xml ───────────────────────────────────────

    [Fact]
    public async Task Get_SitemapXml_ReturnsXmlWithStaticAndDynamicUrls()
    {
        using var factory = CreateFactory();
        // AllowAutoRedirect=false: критерий «каждый <loc> отвечает прямым 200» проверяется без
        // молчаливой развязки redirect-цепочек (единственное исключение — /docs/, см. ниже).
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync("/sitemap.xml");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("https://example.com/ru/models/xiaomi/mimo-v2.5-pro");
        body.Should().Contain("https://example.com/ru/providers/xiaomi");
        body.Should().Contain("https://example.com/ru/tools/cline");
        body.Should().Contain("<lastmod>2026-08-06</lastmod>");
        body.Should().NotContain("providers/empty");
        body.Should().NotContain("tools/draft");

        // Английская ветка адресуема и обязана быть в карте — иначе её нечем обнаружить.
        body.Should().Contain("<loc>https://example.com/en/models/xiaomi/mimo-v2.5-pro</loc>");
        body.Should().Contain("<loc>https://example.com/en/providers/xiaomi</loc>");
        body.Should().Contain("<loc>https://example.com/en/tools/cline</loc>");

        // Русская ветка адресуема под /ru.
        body.Should().Contain("<loc>https://example.com/ru/models/xiaomi/mimo-v2.5-pro</loc>");
        body.Should().Contain("<loc>https://example.com/ru/providers/xiaomi</loc>");
        body.Should().Contain("<loc>https://example.com/ru/tools/cline</loc>");

        // Критерий: каждый URL из sitemap реально отдаёт 200. Абсолютный origin (https://example.com)
        // в тестовом хосте недоступен — ходим по локальному пути к тому же приложению.
        // /docs/ — единственное исключение: в проде его отдаёт контейнер VitePress напрямую,
        // а в тестовом хосте WebApp /docs/ канонизируется 301-редиректом на /docs
        // (существующий redirect-контур не меняем).
        var doc = XDocument.Parse(body);
        body.Should().Contain("<loc>https://example.com/docs/</loc>");

        foreach (var loc in doc.Root!.Elements()
                     .Select(u => u.Element(doc.Root.Name.Namespace + "loc")!.Value))
        {
            var path = new Uri(loc).PathAndQuery;
            using var page = await client.GetAsync(path);
            if (path == "/docs/")
            {
                page.StatusCode.Should().Be(HttpStatusCode.MovedPermanently,
                    "the WebApp host must keep redirecting /docs/ to /docs");
                page.Headers.Location!.OriginalString.Should().Be("/docs");
                continue;
            }

            page.StatusCode.Should().Be(HttpStatusCode.OK, $"sitemap URL {loc} should return 200");
        }
    }

    [Fact]
    public async Task Get_SitemapXml_EveryLocUsesSiteHost_NeverTheApiHost()
    {
        // The API base is admin-configured on a subdomain; the sitemap must only ever list the
        // crawler-facing site host. A leak of the API host here is what broke the old prod sitemap.
        using var factory = CreateFactory(apiBaseUrl: "https://api.example.com", siteBaseUrl: "https://example.com");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/sitemap.xml");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("api.example.com");

        var doc = XDocument.Parse(body);
        var locs = doc.Root!.Elements()
            .Select(u => u.Element(doc.Root.Name.Namespace + "loc")!.Value)
            .ToList();
        locs.Should().NotBeEmpty();
        locs.Should().OnlyContain(loc => loc.StartsWith("https://example.com", StringComparison.Ordinal));
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        CreateFactory("https://example.com", "https://example.com");

    private static WebApplicationFactory<Program> CreateFactory(string apiBaseUrl, string siteBaseUrl)
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
                    ["PublicApiBaseUrl"] = apiBaseUrl,
                    ["PublicSiteBaseUrl"] = siteBaseUrl,
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

    private static AsyncUnaryCall<T> UnaryCall<T>(T response) =>
        new(Task.FromResult(response), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
}
