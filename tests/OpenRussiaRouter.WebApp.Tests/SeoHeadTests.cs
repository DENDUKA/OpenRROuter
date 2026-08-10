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
    public async Task Get_Root_EmitsReciprocalHreflangCluster_WithEnglishXDefault()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        body.Should().Contain("<link rel=\"alternate\" hreflang=\"ru\" href=\"https://example.com/\" />");
        body.Should().Contain("<link rel=\"alternate\" hreflang=\"en\" href=\"https://example.com/en\" />");
        // x-default targets the version for visitors whose language matches neither. Outside the
        // RU-speaking market English is the better landing, so it must not point at the Russian page.
        body.Should().Contain("<link rel=\"alternate\" hreflang=\"x-default\" href=\"https://example.com/en\" />");
    }

    [Fact]
    public async Task Get_EnglishPage_CanonicalIsSelfReferencing_NotTheRussianTwin()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/en/models");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        // Canonicalizing /en/models onto /models would tell search engines the English tree is a
        // duplicate — it would be crawled and then dropped straight back out of the index.
        body.Should().Contain("<link rel=\"canonical\" href=\"https://example.com/en/models\" />");
        body.Should().NotContain("<link rel=\"canonical\" href=\"https://example.com/models\" />");
        body.Should().Contain("property=\"og:url\" content=\"https://example.com/en/models\"");
        body.Should().NotContain("name=\"robots\"");
    }

    [Fact]
    public async Task Get_BothLocales_AdvertiseTheIdenticalAlternateSet()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var ru = await client.GetAsync("/models");
        using var en = await client.GetAsync("/en/models");

        var ruLinks = ExtractHreflangLinks(WebUtility.HtmlDecode(await ru.Content.ReadAsStringAsync()));
        var enLinks = ExtractHreflangLinks(WebUtility.HtmlDecode(await en.Content.ReadAsStringAsync()));

        // A cluster whose members disagree about its membership is discarded wholesale.
        ruLinks.Should().Equal(enLinks);
        ruLinks.Should().Equal(
            ("ru", "https://example.com/models"),
            ("en", "https://example.com/en/models"),
            ("x-default", "https://example.com/en/models"));
    }

    [Fact]
    public async Task Get_NoIndexPage_EmitsNoHreflang()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/login");
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        body.Should().Contain("name=\"robots\" content=\"noindex,follow\"");
        body.Should().NotContain("hreflang");
    }

    private static List<(string Lang, string Href)> ExtractHreflangLinks(string body) =>
        Regex.Matches(body, "<link rel=\"alternate\" hreflang=\"([^\"]+)\" href=\"([^\"]+)\" />")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value))
            .ToList();

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

    [Fact]
    public async Task Get_Root_NoAnalyticsConfigured_EmitsNoTrackingTags()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("googletagmanager.com");
        body.Should().NotContain("mc.yandex.ru");
        body.Should().NotContain("AnalyticsEvents");
    }

    [Fact]
    public async Task Get_Root_AnalyticsConfigured_EmitsBothTrackingTags()
    {
        using var factory = CreateFactory(googleAnalytics: true, yandexMetrika: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("https://www.googletagmanager.com/gtag/js?id=G-TEST123");
        body.Should().Contain("gtag('config', 'G-TEST123')");
        body.Should().Contain("https://mc.yandex.ru/metrika/tag.js?id=11111111");
        body.Should().Contain("https://mc.yandex.ru/watch/11111111");
    }

    [Fact]
    public async Task Get_Root_GoogleAnalyticsConfigured_CspAllowsGa4Endpoints()
    {
        using var factory = CreateFactory(googleAnalytics: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var csp = response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle().Subject;
        csp.Should().Contain("connect-src 'self' https://pay.yandex.ru https://www.googletagmanager.com https://*.google-analytics.com https://analytics.google.com");
        csp.Should().Contain("script-src 'self' 'unsafe-inline' https://pay.yandex.ru https://www.googletagmanager.com");
        csp.Should().NotContain("mc.yandex");
        csp.Should().NotContain("wss://");
    }

    [Fact]
    public async Task Get_Root_NoAnalyticsConfigured_CspHasNoAnalyticsHosts()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var csp = response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle().Subject;
        csp.Should().NotContain("googletagmanager.com");
        csp.Should().NotContain("google-analytics.com");
        csp.Should().NotContain("analytics.google.com");
        csp.Should().NotContain("mc.yandex");
        csp.Should().NotContain("yastatic.net");
        csp.Should().NotContain("wss://");
    }

    [Fact]
    public async Task Get_Root_YandexMetrikaConfigured_CspHasYandexHostsAndNoGoogle()
    {
        using var factory = CreateFactory(yandexMetrika: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var csp = response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle().Subject;
        csp.Should().Contain("https://mc.yandex.ru https://mc.yandex.com https://mc.webvisor.org https://mc.webvisor.com https://yastatic.net");
        csp.Should().Contain("wss://mc.yandex.ru wss://mc.yandex.com wss://mc.webvisor.org wss://mc.webvisor.com");
        csp.Should().NotContain("googletagmanager.com");
        csp.Should().NotContain("google-analytics.com");
        csp.Should().NotContain("analytics.google.com");
    }

    [Fact]
    public async Task Get_Root_BothAnalyticsConfigured_CspAllowsBothGroups()
    {
        using var factory = CreateFactory(googleAnalytics: true, yandexMetrika: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var csp = response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle().Subject;
        csp.Should().Contain("https://*.google-analytics.com");
        csp.Should().Contain("https://analytics.google.com");
        csp.Should().Contain("https://mc.yandex.ru https://mc.yandex.com https://mc.webvisor.org https://mc.webvisor.com https://yastatic.net");
        csp.Should().Contain("wss://mc.yandex.ru wss://mc.yandex.com wss://mc.webvisor.org wss://mc.webvisor.com");
    }

    [Fact]
    public async Task Get_Root_ApiBaseUrlNeverLeaksIntoCrawlerMeta()
    {
        // The admin-configured API base lives on a subdomain; the crawler-facing origin is the
        // apex site. None of the SEO metadata (canonical, og:url, og:image, twitter:image) may
        // reference the API host.
        using var factory = CreateFactory(apiBaseUrl: "https://api.example.com", siteBaseUrl: "https://example.com");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("<link rel=\"canonical\" href=\"https://example.com/\" />");
        body.Should().Contain("property=\"og:url\" content=\"https://example.com/\"");
        body.Should().Contain("property=\"og:image\" content=\"https://example.com/og-image.png\"");
        body.Should().Contain("name=\"twitter:image\" content=\"https://example.com/og-image.png\"");
        body.Should().NotContain("api.example.com");
    }

    [Fact]
    public async Task Get_ModelDetails_ApiExamplesKeepApiHostWhileCanonicalUsesSiteHost()
    {
        using var factory = CreateFactory(apiBaseUrl: "https://api.example.com", siteBaseUrl: "https://example.com");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/models/xiaomi/mimo-v2.5-pro");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        // Integration snippets must still point at the API host (that's what users call).
        body.Should().Contain("base_url=\"https://api.example.com/v1\"");
        body.Should().Contain("https://api.example.com/v1/chat/completions");
        // ... while the canonical stays on the crawler-facing site host.
        body.Should().Contain("<link rel=\"canonical\" href=\"https://example.com/models/xiaomi/mimo-v2.5-pro\" />");
        body.Should().Contain("property=\"og:url\" content=\"https://example.com/models/xiaomi/mimo-v2.5-pro\"");
    }

    [Fact]
    public async Task Get_Root_EmitsOrganizationAndWebSiteJsonLd()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var body = WebUtility.HtmlDecode(await client.GetStringAsync("/"));

        body.Should().Contain("<script type=\"application/ld+json\">");
        body.Should().Contain("\"@type\":\"Organization\"");
        body.Should().Contain("\"@type\":\"WebSite\"");
    }

    [Fact]
    public async Task Get_ModelDetails_EmitsProductAndBreadcrumbJsonLd()
    {
        using var factory = CreateFactory(apiBaseUrl: "https://api.example.com", siteBaseUrl: "https://example.com");
        using var client = factory.CreateClient();

        var body = WebUtility.HtmlDecode(await client.GetStringAsync("/models/xiaomi/mimo-v2.5-pro"));

        body.Should().Contain("\"@type\":\"Product\"");
        body.Should().Contain("\"sku\":\"xiaomi/mimo-v2.5-pro\"");
        body.Should().Contain("\"@type\":\"Offer\"");
        body.Should().Contain("\"priceCurrency\":\"USD\"");
        body.Should().Contain("\"@type\":\"BreadcrumbList\"");
        // Structured-data URLs must track the canonical, never the API host.
        body.Should().Contain("\"url\":\"https://example.com/models/xiaomi/mimo-v2.5-pro\"");
    }

    [Fact]
    public async Task Get_Login_NoIndexPage_EmitsNoJsonLd()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync("/login");

        body.Should().NotContain("application/ld+json");
    }

    [Fact]
    public async Task Get_ModelsCatalog_LinksModelDetailPagesWithRealAnchors()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync("/models");

        // The row's onclick is not a crawlable link: without an <a href> every model detail page
        // is an orphan reachable only from the sitemap.
        body.Should().Contain("href=\"/models/xiaomi/mimo-v2.5-pro\"");
        body.Should().Contain("href=\"/providers/xiaomi\"");
    }

    [Theory]
    [InlineData("/Models", "/models")]
    [InlineData("/models/", "/models")]
    [InlineData("/providers/", "/providers")]
    public async Task Get_DuplicateUrlForm_RedirectsPermanentlyToCanonical(string requested, string expected)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync(requested);

        response.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.OriginalString.Should().Be(expected);
    }

    [Fact]
    public async Task Get_DuplicateUrlForm_PreservesQueryStringOnRedirect()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync("/Models/?category=text");

        response.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.OriginalString.Should().Be("/models?category=text");
    }

    private static WebApplicationFactory<Program> CreateFactory(bool googleAnalytics = false, bool yandexMetrika = false) =>
        CreateFactory("https://example.com", "https://example.com", googleAnalytics, yandexMetrika);

    private static WebApplicationFactory<Program> CreateFactory(
        string apiBaseUrl,
        string siteBaseUrl,
        bool googleAnalytics = false,
        bool yandexMetrika = false)
    {
        var billing = new FakeBillingClient();
        var content = new FakeContentClient();
        var collector = new FakeCollectorClient();

        var config = new Dictionary<string, string?>
        {
            ["PublicApiBaseUrl"] = apiBaseUrl,
            ["PublicSiteBaseUrl"] = siteBaseUrl,
            // appsettings.json hardcodes the prod tag IDs, so each scenario must pin its own
            // values (null = explicitly not configured, overrides the lower-priority source).
            ["Analytics:GoogleTagId"] = googleAnalytics ? "G-TEST123" : null,
            ["Analytics:YandexCounterId"] = yandexMetrika ? "11111111" : null,
        };

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(config);
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
