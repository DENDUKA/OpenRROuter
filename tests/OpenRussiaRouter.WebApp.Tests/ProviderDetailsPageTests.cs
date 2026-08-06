using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.WebApp.Pages;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Services.Currency;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;
using AuthProto = OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using BillingProto = OpenRussiaRouter.Contracts.Grpc.Billing.V1;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class ProviderDetailsPageTests
{
    [Fact]
    public async Task OnGetAsync_ExistingProvider_ReturnsOk()
    {
        var billingClient = new FakeBillingClient(new BillingProto.ModelProvider
        {
            Id = "provider-1",
            DisplayName = "Provider One",
            UpdatedAtUtc = "2026-08-06T00:00:00.0000000Z"
        });
        var model = NewModel(billingClient, "provider-1");

        await model.OnGetAsync();

        model.Provider.Should().NotBeNull();
        model.Provider!.DisplayName.Should().Be("Provider One");
        model.ErrorMessage.Should().BeNull();
        model.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task OnGetAsync_MissingProvider_Returns404()
    {
        var billingClient = new FakeBillingClient(provider: null);
        var model = NewModel(billingClient, "ghost");

        await model.OnGetAsync();

        model.Provider.Should().BeNull();
        model.ErrorMessage.Should().NotBeNull();
        model.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    private static ProviderDetailsModel NewModel(FakeBillingClient billingClient, string id)
    {
        var billingProvider = new BillingProvider(billingClient);
        var currencyFormatter = new CurrencyFormatter(CreateRateProvider(91.5m, 103.0m));
        var authProvider = new AuthProvider(new NullAuthClient());
        var model = new ProviderDetailsModel(
            billingProvider,
            currencyFormatter,
            authProvider,
            NullStringLocalizer<SharedResource>.Instance,
            NullLogger<ProviderDetailsModel>.Instance)
        {
            Id = id
        };
        model.PageContext = new PageContext { HttpContext = new DefaultHttpContext() };
        return model;
    }

    private static CbrExchangeRateProvider CreateRateProvider(decimal rubPerUsd, decimal rubPerEur)
    {
        var snapshot = new CbrRateSnapshot();
        snapshot.Update(rubPerUsd, rubPerEur);
        return new CbrExchangeRateProvider(snapshot);
    }

    private sealed class FakeBillingClient(BillingProto.ModelProvider? provider) : BillingGrpc.BillingServiceClient
    {
        public override AsyncUnaryCall<BillingProto.GetProviderResponse> GetModelProviderAsync(
            BillingProto.GetProviderRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var response = new BillingProto.GetProviderResponse { Found = provider is not null };
            if (provider is not null)
            {
                response.Provider = provider;
            }

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

        public override AsyncUnaryCall<BillingProto.ListTagsResponse> ListTagsAsync(
            BillingProto.ListTagsRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return UnaryCall(new BillingProto.ListTagsResponse());
        }
    }

    private sealed class NullAuthClient : AuthGrpc.AuthServiceClient
    {
        public override AsyncUnaryCall<AuthProto.GetUserResponse> GetUserAsync(
            AuthProto.GetUserRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return UnaryCall(new AuthProto.GetUserResponse());
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
