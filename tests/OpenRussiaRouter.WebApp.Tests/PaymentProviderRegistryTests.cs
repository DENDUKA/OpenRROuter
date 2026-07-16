using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Services.Payments;

namespace OpenRussiaRouter.WebApp.Tests;

/// <summary>
/// Minimal IPaymentProvider implementation for registry/guard/checkout tests — only Name and
/// SettlementCurrency matter.
/// </summary>
internal sealed class StubProvider : IPaymentProvider
{
    private readonly string _name;
    private readonly string? _settlementCurrency;

    public StubProvider(string name, string? settlementCurrency = null)
    {
        _name = name;
        _settlementCurrency = settlementCurrency;
    }

    public string Name => _name;
    public string? SettlementCurrency => _settlementCurrency;
    public Task<PaymentCreated> CreateAsync(PaymentRequest request, CancellationToken ct)
        => Task.FromResult(new PaymentCreated(
            ProviderPaymentId: $"stub_{request.OrderId}",
            PayUrl: $"/pay/stub/{request.OrderId}"));
    public WebhookVerificationResult Verify(string rawBody, Microsoft.AspNetCore.Http.IHeaderDictionary headers)
        => WebhookVerificationResult.Valid;
    public PaymentWebhookEvent Parse(string rawBody) => throw new NotImplementedException();
    public Task<ProviderPaymentStatus> GetPaymentAsync(string providerPaymentId, CancellationToken ct)
        => Task.FromResult(new ProviderPaymentStatus(ProviderPaymentStatusType.Created, null, null, null, null));
}

public class PaymentProviderRegistryTests
{
    private static IOptions<PaymentSettings> Opts(string active) => Microsoft.Extensions.Options.Options.Create(new PaymentSettings
    {
        ActiveProvider = active
    });

    [Fact]
    public void Active_returns_null_when_setting_is_empty()
    {
        var registry = new PaymentProviderRegistry(
            new IPaymentProvider[] { new StubProvider("mock") },
            Opts(""));

        registry.Active.Should().BeNull();
    }

    [Fact]
    public void Active_returns_provider_matching_setting()
    {
        var mock = new StubProvider("mock");
        var registry = new PaymentProviderRegistry(new[] { mock }, Opts("mock"));

        registry.Active.Should().BeSameAs(mock);
    }

    [Fact]
    public void Active_is_case_insensitive()
    {
        var mock = new StubProvider("mock");
        var registry = new PaymentProviderRegistry(new[] { mock }, Opts("MOCK"));

        registry.Active.Should().BeSameAs(mock);
    }

    [Fact]
    public void TryGet_returns_null_for_unknown_provider()
    {
        var registry = new PaymentProviderRegistry(
            new IPaymentProvider[] { new StubProvider("mock") },
            Opts("mock"));

        registry.TryGet("nonexistent").Should().BeNull();
    }

    [Fact]
    public void Get_throws_for_unknown_provider()
    {
        var registry = new PaymentProviderRegistry(
            new IPaymentProvider[] { new StubProvider("mock") },
            Opts("mock"));

        var act = () => registry.Get("nonexistent");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_throws_for_empty_name()
    {
        var broken = new StubProvider("");
        var act = () => new PaymentProviderRegistry(new[] { broken }, Opts(""));
        act.Should().Throw<InvalidOperationException>();
    }
}
