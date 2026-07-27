using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.WebApp.Pages.Admin;
using OpenRussiaRouter.WebApp.Services.Auth;
using OpenRussiaRouter.WebApp.Services.Billing;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;
using AuthProto = OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using BillingProto = OpenRussiaRouter.Contracts.Grpc.Billing.V1;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class TopUpsPageTests
{
    [Fact]
    public async Task OnGetAsync_UserQueryWithoutMatches_ReturnsEmptyWithoutCallingBilling()
    {
        var billingClient = new CapturingBillingClient();
        var model = NewModel(billingClient);
        model.UserQuery = "missing@example.test";

        await model.OnGetAsync();

        billingClient.CallCount.Should().Be(0);
        model.Records.Should().BeEmpty();
        model.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task OnGetAsync_WithoutUserQuery_LoadsUnfilteredTopUps()
    {
        var billingClient = new CapturingBillingClient();
        var model = NewModel(billingClient);

        await model.OnGetAsync();

        billingClient.CallCount.Should().Be(1);
        billingClient.CapturedRequest!.UserIds.Should().BeEmpty();
    }

    private static TopUpsModel NewModel(CapturingBillingClient billingClient)
    {
        var authProvider = new AuthProvider(new SingleUserAuthClient());
        var billingProvider = new BillingProvider(billingClient);
        return new TopUpsModel(billingProvider, authProvider, NullLogger<TopUpsModel>.Instance);
    }

    private sealed class SingleUserAuthClient : AuthGrpc.AuthServiceClient
    {
        public override AsyncUnaryCall<AuthProto.ListUsersResponse> ListUsersAsync(
            AuthProto.ListUsersRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var response = new AuthProto.ListUsersResponse
            {
                Users =
                {
                    new AuthProto.UserInfo
                    {
                        Id = "user-1",
                        Email = "known@example.test",
                        Role = "User",
                        Status = "Active",
                        CreatedAtUnixTimeSeconds = 1,
                        MaxApiKeys = 3
                    }
                }
            };
            return UnaryCall(response);
        }
    }

    private sealed class CapturingBillingClient : BillingGrpc.BillingServiceClient
    {
        public int CallCount { get; private set; }
        public BillingProto.ListAllTopUpsRequest? CapturedRequest { get; private set; }

        public override AsyncUnaryCall<BillingProto.ListAllTopUpsResponse> ListAllTopUpsAsync(
            BillingProto.ListAllTopUpsRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            CapturedRequest = request;
            return UnaryCall(new BillingProto.ListAllTopUpsResponse());
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
