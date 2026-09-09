using System.Net;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;
using BillingProto = OpenRussiaRouter.Contracts.Grpc.Billing.V1;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class UserMenuLayoutTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task AuthenticatedPage_RendersUserMenuWithA11yAttributes()
    {
        var billing = new FakeBillingClient("0.50");
        using var authedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<BillingGrpc.BillingServiceClient>();
                services.AddSingleton<BillingGrpc.BillingServiceClient>(billing);
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName, _ => { });
            }));

        using var client = authedFactory.CreateClient();

        using var response = await client.GetAsync("/workspace/settings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("id=\"user-menu\"");
        body.Should().Contain("id=\"user-menu-trigger\"");
        body.Should().Contain("id=\"user-dropdown\"");
        body.Should().Contain("aria-haspopup=\"menu\"");
        body.Should().Contain("aria-expanded=\"false\"");
        body.Should().Contain("role=\"menu\"");
        body.Should().Contain("role=\"menuitem\"");
        body.Should().Contain("user-menu--open");
        body.Should().Contain("class=\"user-balance\"");
        body.Should().Contain("$0.50");
        billing.Calls.Should().Be(1);
    }

    [Fact]
    public async Task AnonymousPage_DoesNotRenderOrRequestBalance()
    {
        var billing = new FakeBillingClient("0.50");
        using var anonymousFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<BillingGrpc.BillingServiceClient>();
                services.AddSingleton<BillingGrpc.BillingServiceClient>(billing);
            }));

        using var response = await anonymousFactory.CreateClient().GetAsync("/login");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("user-balance");
        billing.Calls.Should().Be(0);
    }

    private sealed class FakeBillingClient(string balanceUsd) : BillingGrpc.BillingServiceClient
    {
        public int Calls { get; private set; }

        public override AsyncUnaryCall<BillingProto.GetBalanceResponse> GetBalanceAsync(
            BillingProto.GetBalanceRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return UnaryCall(Task.FromResult(new BillingProto.GetBalanceResponse
            {
                BalanceUsd = balanceUsd
            }));
        }

        private static AsyncUnaryCall<T> UnaryCall<T>(Task<T> response) =>
            new(response, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
    }
}
