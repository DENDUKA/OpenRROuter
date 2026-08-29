using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.WebApp.Pages;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using BillingProto = OpenRussiaRouter.Contracts.Grpc.Billing.V1;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class IndexPageTests
{
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
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new PageActionDescriptor(),
            new ModelStateDictionary());

        return new IndexModel(
            new BillingProvider(client),
            new AuthProvider(client: default!),
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
