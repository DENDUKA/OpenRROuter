using System.Globalization;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.WebApp.Pages;
using OpenRussiaRouter.WebApp.Services;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using BillingProto = OpenRussiaRouter.Contracts.Grpc.Billing.V1;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class IndexPageTests
{
    [Theory]
    [InlineData("1", "2", "0", "3", "5", "0", 67)]
    [InlineData("7.5", "4", "0", "10", "10", "0", 60)]
    [InlineData("10", "10", "0.5", "10", "10", "1", 50)]
    [InlineData("5", "10", "1", "10", "20", "2", 50)]
    [InlineData("7.55", "10", "0", "10", "10", "0", 25)]
    [InlineData("7.56", "10", "0", "10", "10", "0", 24)]
    [InlineData("9.96", "10", "0", "10", "10", "0", null)]
    [InlineData("10", "10", "1", "10", "10", "1", null)]
    [InlineData("11", "11", "2", "10", "10", "1", null)]
    [InlineData("0", "0", "0", "0", "0", "0", null)]
    [InlineData("1", "2", "3", "0", "0", "0", null)]
    [InlineData("0", "10", "0", "2", "10", "0", 100)]
    [InlineData("-1", "4", "0", "10", "10", "-1", 60)]
    public void FlagshipCard_CalculatesRoundedMaximumDiscount(
        string input, string output, string cache,
        string referenceInput, string referenceOutput, string referenceCache,
        int? expectedDiscount)
    {
        var card = new IndexModel.FlagshipModelCard(
            "test-model", "Test model", "Provider", "provider", "/models/provider/test-model",
            decimal.Parse(input, CultureInfo.InvariantCulture),
            decimal.Parse(output, CultureInfo.InvariantCulture),
            decimal.Parse(cache, CultureInfo.InvariantCulture),
            decimal.Parse(referenceInput, CultureInfo.InvariantCulture),
            decimal.Parse(referenceOutput, CultureInfo.InvariantCulture),
            decimal.Parse(referenceCache, CultureInfo.InvariantCulture));

        card.MaxDiscountPercent.Should().Be(expectedDiscount);
    }

    [Fact]
    public async Task OnGetAsync_SelectsCompleteFlagshipsAndSortsByPriorityThenIdAsync()
    {
        var client = new FakeBillingClient([
            CreateModel("flagship-low", priority: 1, isFlagship: true, "3", "5", "0"),
            CreateModel("flagship-z", priority: 10, isFlagship: true, "4", "6", "1"),
            CreateModel("standard", priority: 100, isFlagship: false, "7", "8", "2"),
            CreateModel("flagship-incomplete", priority: 50, isFlagship: true, "7", "8", ""),
            CreateModel("flagship-a", priority: 10, isFlagship: true, "2", "4", "0.25")
        ]);
        var page = CreatePage(client);

        await page.OnGetAsync();

        page.FlagshipModels.Select(static model => model.Id)
            .Should().Equal("flagship-a", "flagship-z", "flagship-low");
        page.FlagshipModels[0].OpenRouterCachePrice.Should().Be(0.25m);
        page.FlagshipModels[0].Path.Should().Be("/models/test-provider/flagship-a");
        page.V1BaseUrl.Should().Be("https://test.local/v1");
    }

    [Fact]
    public async Task OnGetAsync_WhenBillingFails_LeavesFlagshipShowcaseEmptyAsync()
    {
        var page = CreatePage(new FakeBillingClient([], failModelsRequest: true));

        var act = page.OnGetAsync;

        await act.Should().NotThrowAsync();
        page.FlagshipModels.Should().BeEmpty();
    }

    private static IndexModel CreatePage(FakeBillingClient client)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("test.local");

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new PageActionDescriptor(),
            new ModelStateDictionary());

        var billingProvider = new BillingProvider(client);
        var configuration = new ConfigurationBuilder().Build();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new ApiBaseUrlResolver(
            configuration,
            billingProvider,
            memoryCache,
            NullLogger<ApiBaseUrlResolver>.Instance);

        return new IndexModel(
            billingProvider,
            new AuthProvider(client: default!),
            resolver,
            NullLogger<IndexModel>.Instance)
        {
            PageContext = new PageContext(actionContext)
        };
    }

    private static BillingProto.PublicModel CreateModel(
        string id,
        int priority,
        bool isFlagship,
        string openRouterInput,
        string openRouterOutput,
        string openRouterCache) =>
        new()
        {
            Id = id,
            DisplayName = id,
            UpstreamModelId = $"router/{id}",
            InputPricePer1MUsd = "1",
            OutputPricePer1MUsd = "2",
            CachePricePer1MUsd = "0",
            Provider = "Test Provider",
            ProviderId = "test-provider",
            Priority = priority,
            IsFlagship = isFlagship,
            OpenrouterInputPricePer1MUsd = openRouterInput,
            OpenrouterOutputPricePer1MUsd = openRouterOutput,
            OpenrouterCachePricePer1MUsd = openRouterCache
        };

    private sealed class FakeBillingClient(
        IReadOnlyList<BillingProto.PublicModel> models,
        bool failModelsRequest = false) : BillingGrpc.BillingServiceClient
    {
        public override AsyncUnaryCall<BillingProto.ListPublicModelsResponse> ListPublicModelsAsync(
            BillingProto.ListPublicModelsRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            if (failModelsRequest)
            {
                return UnaryCall<BillingProto.ListPublicModelsResponse>(
                    Task.FromException<BillingProto.ListPublicModelsResponse>(
                        new RpcException(new Status(StatusCode.Unavailable, "Billing unavailable"))));
            }

            var response = new BillingProto.ListPublicModelsResponse();
            response.Models.AddRange(models);
            return UnaryCall(Task.FromResult(response));
        }

        public override AsyncUnaryCall<BillingProto.ListProvidersResponse> ListModelProvidersAsync(
            BillingProto.ListProvidersRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default) =>
            UnaryCall(Task.FromResult(new BillingProto.ListProvidersResponse()));

        public override AsyncUnaryCall<BillingProto.GetPromotionSettingsResponse> GetPromotionSettingsAsync(
            BillingProto.GetPromotionSettingsRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default) =>
            UnaryCall(Task.FromResult(new BillingProto.GetPromotionSettingsResponse()));

        private static AsyncUnaryCall<T> UnaryCall<T>(Task<T> response) =>
            new(response, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
    }
}
