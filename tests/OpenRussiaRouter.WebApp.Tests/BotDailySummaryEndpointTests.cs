using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.WebApp.Services.Billing;
using OpenRussiaRouter.WebApp.Support;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;

namespace OpenRussiaRouter.WebApp.Tests;

public sealed class BotDailySummaryEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BotDailySummaryEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Configure<SupportBotSettings>(opts =>
                {
                    opts.Enabled = true;
                    opts.AuthToken = "test-bot-token-123";
                });

                services.AddSingleton<BillingProvider>(new FakeBillingProvider());
            });
        });
    }

    [Fact]
    public async Task Daily_summary_requires_authorization()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync("/internal/bot/daily-summary");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Daily_summary_returns_active_api_users_when_authorized()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/bot/daily-summary");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-bot-token-123");

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("activeApiUsers").GetInt32().Should().Be(8);
        root.GetProperty("topupsCount").GetInt32().Should().Be(5);
        root.GetProperty("topupsTotalRub").GetDecimal().Should().Be(1250m);
        root.GetProperty("topupsTotalUsd").GetDecimal().Should().Be(14.5m);
        root.GetProperty("periodHours").GetInt32().Should().Be(24);
    }

    private sealed class TestBillingClient : BillingGrpc.BillingServiceClient { }

    private sealed class FakeBillingProvider : BillingProvider
    {
        public FakeBillingProvider() : base(new TestBillingClient()) { }

        public override Task<(int TopupsCount, decimal TotalRub, decimal TotalUsd, int ActiveApiUsers)> GetDailyStatsAsync(
            DateTimeOffset sinceUtc, CancellationToken cancellationToken = default)
        {
            return Task.FromResult((5, 1250m, 14.5m, 8));
        }
    }
}
