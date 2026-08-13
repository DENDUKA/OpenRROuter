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

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// <c>/models/{provider}/{slug}</c> над каталогом продовой формы: публичный id модели — это не
/// <c>"{provider}/{slug}"</c>, он бывает как без префикса (<c>claude-opus-4-8</c>), так и с ним
/// (<c>RWT/Gemini-3.1-Pro</c>). Страница обязана резолвить оба и канонизировать старые ссылки.
/// </summary>
public sealed class ModelDetailsPageTests
{
    [Theory]
    [InlineData("/models/antropic/claude-opus-4-8", "claude-opus-4-8")]
    [InlineData("/models/google/Gemini-3.1-Pro", "RWT/Gemini-3.1-Pro")]
    public async Task Get_CanonicalUrl_RendersModel(string url, string expectedPublicId)
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain(expectedPublicId);
    }

    [Theory]
    // Ссылка старого каталога: отображаемое имя провайдера в сегменте.
    [InlineData("/models/Antropic/claude-opus-4-8", "/models/antropic/claude-opus-4-8")]
    // Ссылка старого sitemap: префикс id вместо провайдера.
    [InlineData("/models/RWT/Gemini-3.1-Pro", "/models/google/Gemini-3.1-Pro")]
    // Чужой провайдер в сегменте: слаг определяет модель однозначно.
    [InlineData("/models/openai/claude-opus-4-8", "/models/antropic/claude-opus-4-8")]
    public async Task Get_LegacyUrl_RedirectsToCanonical(string url, string expectedLocation)
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.ToString().Should().Be(expectedLocation);
    }

    [Fact]
    public async Task Get_UnknownModel_Returns404()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/models/antropic/no-such-model");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var billing = new FakeBillingClient();
        var collector = new FakeCollectorClient();

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PublicApiBaseUrl"] = "https://example.com",
                    ["PublicSiteBaseUrl"] = "https://example.com",
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<BillingGrpc.BillingServiceClient>();
                services.AddSingleton<BillingGrpc.BillingServiceClient>(billing);
                services.RemoveAll<CollectorGrpc.CollectorClient>();
                services.AddSingleton<CollectorGrpc.CollectorClient>(collector);
            });
        });
    }

    private sealed class FakeBillingClient : BillingGrpc.BillingServiceClient
    {
        private static readonly (string Id, string DisplayName, string Provider, string ProviderId)[] Catalog =
        [
            ("claude-opus-4-8", "Claude Opus 4.8", "Antropic", "antropic"),
            ("RWT/Gemini-3.1-Pro", "Gemini 3.1 Pro", "Google", "google"),
        ];

        public override AsyncUnaryCall<BillingProto.ListPublicModelsResponse> ListPublicModelsAsync(
            BillingProto.ListPublicModelsRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var response = new BillingProto.ListPublicModelsResponse();
            foreach (var (id, displayName, provider, providerId) in Catalog)
            {
                response.Models.Add(new BillingProto.PublicModel
                {
                    Id = id,
                    DisplayName = displayName,
                    UpstreamModelId = id,
                    InputPricePer1MUsd = "1",
                    OutputPricePer1MUsd = "2",
                    CachePricePer1MUsd = "0",
                    Provider = provider,
                    ProviderId = providerId,
                });
            }

            return UnaryCall(response);
        }

        public override AsyncUnaryCall<BillingProto.GetModelResponse> GetModelAsync(
            BillingProto.GetModelRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var response = new BillingProto.GetModelResponse();
            foreach (var (id, displayName, provider, providerId) in Catalog)
            {
                if (!string.Equals(id, request.ModelId, StringComparison.Ordinal))
                {
                    continue;
                }

                response.Found = true;
                response.Model = new BillingProto.ModelDetail
                {
                    Id = id,
                    DisplayName = displayName,
                    UpstreamModelId = id,
                    Provider = provider,
                    ProviderId = providerId,
                };
                break;
            }

            return UnaryCall(response);
        }

        public override AsyncUnaryCall<BillingProto.ListProvidersResponse> ListModelProvidersAsync(
            BillingProto.ListProvidersRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var response = new BillingProto.ListProvidersResponse();
            response.Providers.Add(new BillingProto.ModelProvider { Id = "antropic", DisplayName = "Antropic" });
            response.Providers.Add(new BillingProto.ModelProvider { Id = "google", DisplayName = "Google" });
            return UnaryCall(response);
        }

        public override AsyncUnaryCall<BillingProto.ListTagsResponse> ListTagsAsync(
            BillingProto.ListTagsRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default) =>
            UnaryCall(new BillingProto.ListTagsResponse());

        public override AsyncUnaryCall<BillingProto.GetPlatformSettingResponse> GetPlatformSettingAsync(
            BillingProto.GetPlatformSettingRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default) =>
            UnaryCall(new BillingProto.GetPlatformSettingResponse());

        public override AsyncUnaryCall<BillingProto.GetPromotionSettingsResponse> GetPromotionSettingsAsync(
            BillingProto.GetPromotionSettingsRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default) =>
            UnaryCall(new BillingProto.GetPromotionSettingsResponse());
    }

    private sealed class FakeCollectorClient : CollectorGrpc.CollectorClient
    {
        public override AsyncUnaryCall<CollectorProto.ListNodeModelCatalogResponse> ListNodeModelCatalogAsync(
            CollectorProto.ListNodeModelCatalogRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default) =>
            UnaryCall(new CollectorProto.ListNodeModelCatalogResponse());
    }

    private static AsyncUnaryCall<TResponse> UnaryCall<TResponse>(TResponse response)
        where TResponse : class => new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
}
