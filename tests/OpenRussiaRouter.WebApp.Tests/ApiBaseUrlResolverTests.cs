using System.Net;
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

public sealed class ApiBaseUrlResolverTests
{
    [Fact]
    public async Task IntegrationExamples_UsesPlatformSetting_WhenConfigIsEmpty()
    {
        using var factory = CreateFactory(platformApiBaseUrl: "https://api.rawrter.com", configApiBaseUrl: null);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/docs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("api.rawrter.com");
        body.Should().NotContain("api.openrussiarouter.com");
    }

    [Fact]
    public async Task IntegrationExamples_FallsBackToConfig_WhenPlatformSettingIsNull()
    {
        using var factory = CreateFactory(platformApiBaseUrl: null, configApiBaseUrl: "https://api.example.com");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/docs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("api.example.com");
    }

    [Fact]
    public async Task SiteMetadata_NeverContainsApiSubdomain()
    {
        using var factory = CreateFactory(platformApiBaseUrl: "https://api.rawrter.com", configApiBaseUrl: null);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/ru");
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        body.Should().Contain("property=\"og:url\" content=\"https://example.com/ru\"");
        body.Should().NotContain("api.rawrter.com");
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string? platformApiBaseUrl,
        string? configApiBaseUrl)
    {
        var billing = new FakeBillingClient(platformApiBaseUrl);
        var content = new FakeContentClient();
        var collector = new FakeCollectorClient();

        var config = new Dictionary<string, string?>
        {
            ["PublicApiBaseUrl"] = configApiBaseUrl,
            ["PublicSiteBaseUrl"] = "https://example.com",
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
        private readonly string? _platformApiBaseUrl;

        public FakeBillingClient(string? platformApiBaseUrl) => _platformApiBaseUrl = platformApiBaseUrl;

        public override AsyncUnaryCall<BillingProto.GetPlatformSettingResponse> GetPlatformSettingAsync(
            BillingProto.GetPlatformSettingRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var found = request.Key == "public_api_base_url" && _platformApiBaseUrl is not null;
            var response = new BillingProto.GetPlatformSettingResponse
            {
                Found = found,
                Value = _platformApiBaseUrl ?? string.Empty,
            };
            return UnaryCall(response);
        }

        public override AsyncUnaryCall<BillingProto.ListPublicModelsResponse> ListPublicModelsAsync(
            BillingProto.ListPublicModelsRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return UnaryCall(new BillingProto.ListPublicModelsResponse());
        }

        public override AsyncUnaryCall<BillingProto.ListProvidersResponse> ListModelProvidersAsync(
            BillingProto.ListProvidersRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return UnaryCall(new BillingProto.ListProvidersResponse());
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
            return UnaryCall(new ContentProto.ListToolsResponse());
        }

        public override AsyncUnaryCall<ContentProto.ListToolCategoriesResponse> ListToolCategoriesAsync(
            ContentProto.ListToolCategoriesRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return UnaryCall(new ContentProto.ListToolCategoriesResponse());
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
