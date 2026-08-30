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
    public async Task Get_Root_RedirectsToRussianCanonical()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.OriginalString.Should().Be("/ru");
    }

    [Fact]
    public async Task Get_RussianHome_EmitsCanonicalDescriptionAndOgTags()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/ru");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("<link rel=\"canonical\" href=\"https://example.com/ru\" />");
        body.Should().Contain("<meta name=\"description\" content=\"");
        body.Should().Contain("единый OpenAI-совместимый API для работы с AI-моделями");
        body.Should().Contain("property=\"og:site_name\" content=\"Rawrter\"");
        body.Should().Contain("property=\"og:type\" content=\"website\"");
        body.Should().Contain("property=\"og:title\"");
        body.Should().Contain("property=\"og:description\"");
        body.Should().Contain("property=\"og:url\" content=\"https://example.com/ru\"");
        body.Should().Contain("property=\"og:image\" content=\"https://example.com/og-image.png\"");
        body.Should().Contain("name=\"twitter:card\" content=\"summary_large_image\"");
        body.Should().Contain("name=\"twitter:image\" content=\"https://example.com/og-image.png\"");
        body.Should().NotContain("name=\"robots\"");
    }

    [Fact]
    public async Task Get_RussianHome_EmitsReciprocalHreflangCluster_WithEnglishXDefault()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/ru");
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        body.Should().Contain("<link rel=\"alternate\" hreflang=\"ru\" href=\"https://example.com/ru\" />");
        body.Should().Contain("<link rel=\"alternate\" hreflang=\"en\" href=\"https://example.com/en\" />");
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
        body.Should().Contain("<link rel=\"canonical\" href=\"https://example.com/en/models\" />");
        body.Should().NotContain("<link rel=\"canonical\" href=\"https://example.com/ru/models\" />");
        body.Should().Contain("property=\"og:url\" content=\"https://example.com/en/models\"");
        body.Should().NotContain("name=\"robots\"");
    }

    [Fact]
    public async Task Get_EnglishHome_CanonicalIsSelfReferencing_WithoutTrailingSlash()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/en");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("<link rel=\"canonical\" href=\"https://example.com/en\" />");
        body.Should().NotContain("<link rel=\"canonical\" href=\"https://example.com/en/\" />");
        body.Should().Contain("property=\"og:url\" content=\"https://example.com/en\"");
        body.Should().NotContain("name=\"robots\"");

        body.Should().Contain("<link rel=\"alternate\" hreflang=\"ru\" href=\"https://example.com/ru\" />");
        body.Should().Contain("<link rel=\"alternate\" hreflang=\"en\" href=\"https://example.com/en\" />");
        body.Should().Contain("<link rel=\"alternate\" hreflang=\"x-default\" href=\"https://example.com/en\" />");
    }

    [Theory]
    [InlineData("/ru", "Флагманские модели", "Наша цена", "/ru/models/xiaomi/mimo-v2.5-pro", "до")]
    [InlineData("/en", "Flagship models. Lower prices.", "Our price", "/en/models/xiaomi/mimo-v2.5-pro", "up to")]
    public async Task Get_Home_RendersLocalizedFlagshipPriceComparisonAsync(
        string path,
        string title,
        string ourPriceLabel,
        string modelPath,
        string discountQualifier)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain(title);
        body.Should().Contain(ourPriceLabel);
        body.Should().Contain("<del>$3</del>");
        body.Should().Contain("<strong>$1</strong>");
        body.Should().Contain("color: var(--color-success)");
        body.Should().Contain($"href=\"{modelPath}\"");

        var card = Regex.Match(body, "<a class=\"flagship-card\"[\\s\\S]*?</a>").Value;
        card.Should().Contain("class=\"flagship-model-heading\"");
        card.Should().Contain($"<span class=\"flagship-discount-label\">{discountQualifier}</span>");
        Regex.Matches(card, "<strong>−67%</strong>").Count.Should().Be(1);
        card.Should().NotContain("flagship-mark").And.NotContain("Флагман").And.NotContain("Flagship");
        Regex.Match(body, @"\.flagship-price-row del\s*\{[^}]*\}").Value
            .Should().Contain("color: var(--color-danger)").And.Contain("text-decoration-color: currentColor");
        Regex.Match(body, @"\.flagship-discount\s*\{[^}]*\}").Value
            .Should().Contain("color: var(--color-success)");

        var oursIndex = body.IndexOf($"<span>{ourPriceLabel}</span>", StringComparison.Ordinal);
        var openRouterIndex = body.IndexOf("<span>OpenRouter</span>", StringComparison.Ordinal);

        oursIndex.Should().BeGreaterThan(-1);
        openRouterIndex.Should().BeGreaterThan(oursIndex);
        body.Should().Contain("data-flagship-carousel");
        body.Should().Contain("data-flagship-track");
        body.Should().Contain("data-flagship-cycle");
        body.Should().Contain("touch-action: auto;");
        body.Should().Contain("[data-flagship-carousel][data-flagship-initialized=\"true\"] { touch-action: pan-y; }");
        body.Should().NotContain("data-flagship-previous");
        body.Should().NotContain("data-flagship-next");
    }

    [Fact]
    public async Task Get_BothLocales_AdvertiseTheIdenticalAlternateSet()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var ru = await client.GetAsync("/ru/models");
        using var en = await client.GetAsync("/en/models");

        var ruLinks = ExtractHreflangLinks(WebUtility.HtmlDecode(await ru.Content.ReadAsStringAsync()));
        var enLinks = ExtractHreflangLinks(WebUtility.HtmlDecode(await en.Content.ReadAsStringAsync()));

        ruLinks.Should().Equal(enLinks);
        ruLinks.Should().Equal(
            ("ru", "https://example.com/ru/models"),
            ("en", "https://example.com/en/models"),
            ("x-default", "https://example.com/en/models"));
    }

    [Fact]
    public async Task Get_AdminAndWorkspacePages_EmitNoIndex()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/login", "/signup", "/workspace/apikeys", "/admin" })
        {
            using var response = await client.GetAsync(path);
            var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            body.Should().Contain("<meta name=\"robots\" content=\"noindex,follow\" />",
                $"path {path} must not be indexed");
            body.Should().NotContain("rel=\"canonical\"", $"path {path} must not have a canonical link");
            body.Should().NotContain("hreflang", $"path {path} must not advertise alternates");
        }
    }

    [Fact]
    public async Task Get_MissingResource_Returns404WithNoIndex()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/ru/models/non-existent-model-public-id");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("<meta name=\"robots\" content=\"noindex,follow\" />");
        body.Should().NotContain("rel=\"canonical\"");
        body.Should().NotContain("hreflang");
    }

    [Fact]
    public async Task Get_HomePage_EmitsSiteGraph_OrganizationAndWebSite()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/ru");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("\"@type\":\"Organization\"");
        body.Should().Contain("\"name\":\"Rawrter\"");
        body.Should().Contain("\"@type\":\"WebSite\"");
    }

    [Theory]
    [InlineData("/ru/models/xiaomi/mimo-v2.5-pro")]
    [InlineData("/en/models/xiaomi/mimo-v2.5-pro")]
    public async Task Get_ModelDetails_EmitsBreadcrumbsAndProductJsonLd(string url)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("\"@type\":\"BreadcrumbList\"");
        body.Should().Contain("\"@type\":\"Product\"");
        body.Should().Contain("\"name\":\"MiMo V2.5 Pro\"");
    }

    [Theory]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, true, true)]
    [InlineData(false, false, false, false)]
    public async Task Get_Root_EmitsConfiguredAnalyticsTags(
        bool ga, bool ym, bool expectGa, bool expectYm)
    {
        using var factory = CreateFactory(googleAnalytics: ga, yandexMetrika: ym);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/ru");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        if (expectGa)
        {
            body.Should().Contain("https://www.googletagmanager.com/gtag/js?id=G-TEST123");
        }
        else
        {
            body.Should().NotContain("googletagmanager.com/gtag/js");
        }

        if (expectYm)
        {
            body.Should().Contain("https://mc.yandex.ru/metrika/tag.js?id=11111111");
        }
        else
        {
            body.Should().NotContain("mc.yandex.ru/metrika/tag.js");
        }
    }

    [Theory]
    [InlineData("/RU/models", "/ru/models")]
    [InlineData("/EN/models", "/en/models")]
    [InlineData("/ru/models/", "/ru/models")]
    [InlineData("/en/models/", "/en/models")]
    [InlineData("/models", "/ru/models")]
    [InlineData("/providers", "/ru/providers")]
    [InlineData("/models/", "/ru/models")]
    [InlineData("/providers/", "/ru/providers")]
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

        using var response = await client.GetAsync("/ru/Models/?category=text");

        response.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.OriginalString.Should().Be("/ru/models?category=text");
    }

    private static List<(string Culture, string Url)> ExtractHreflangLinks(string body)
    {
        var matches = Regex.Matches(body, "<link rel=\"alternate\" hreflang=\"([^\"]+)\" href=\"([^\"]+)\" />");
        return matches.Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();
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
                Priority = 10,
                IsFlagship = true,
                OpenrouterInputPricePer1MUsd = "3",
                OpenrouterOutputPricePer1MUsd = "5",
                OpenrouterCachePricePer1MUsd = "0",
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
