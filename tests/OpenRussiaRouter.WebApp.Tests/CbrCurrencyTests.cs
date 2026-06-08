using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.WebApp.Services.Currency;

namespace OpenRussiaRouter.WebApp.Tests;

public class CbrCurrencyTests
{
    // --- Parser ---

    private const string CbrXml = """
        <?xml version="1.0" encoding="windows-1251"?>
        <ValCurs Date="08.06.2026" name="Foreign Currency Market">
          <Valute ID="R01235">
            <NumCode>840</NumCode>
            <CharCode>USD</CharCode>
            <Nominal>1</Nominal>
            <Name>Доллар США</Name>
            <Value>91,5000</Value>
          </Valute>
          <Valute ID="R01239">
            <NumCode>978</NumCode>
            <CharCode>EUR</CharCode>
            <Nominal>1</Nominal>
            <Name>Евро</Name>
            <Value>103,0000</Value>
          </Valute>
        </ValCurs>
        """;

    // Nominal=10 variant (e.g., some currencies use 10 as nominal)
    private const string CbrXmlNominal10 = """
        <?xml version="1.0" encoding="windows-1251"?>
        <ValCurs Date="08.06.2026" name="Foreign Currency Market">
          <Valute ID="R01235">
            <CharCode>USD</CharCode>
            <Nominal>10</Nominal>
            <Value>915,0000</Value>
          </Valute>
          <Valute ID="R01239">
            <CharCode>EUR</CharCode>
            <Nominal>10</Nominal>
            <Value>1030,0000</Value>
          </Valute>
        </ValCurs>
        """;

    [Fact]
    public void ParseXml_StandardFixture_ReturnsCorrectRubPerUsd()
    {
        var (rubPerUsd, _) = CbrClient.ParseXml(CbrXml);
        rubPerUsd.Should().Be(91.5m);
    }

    [Fact]
    public void ParseXml_StandardFixture_CommaDecimalSeparatorParsedCorrectly()
    {
        // Value field uses comma as decimal separator per CBR format
        var (rubPerUsd, rubPerEur) = CbrClient.ParseXml(CbrXml);
        rubPerUsd.Should().Be(91.5m);
        rubPerEur.Should().Be(103.0m);
    }

    [Fact]
    public void ParseXml_StandardFixture_CrossRateUsdToEurCorrect()
    {
        var (rubPerUsd, rubPerEur) = CbrClient.ParseXml(CbrXml);
        // USD→EUR = rubPerUsd / rubPerEur
        var usdToEur = rubPerUsd / rubPerEur;
        usdToEur.Should().BeApproximately(91.5m / 103.0m, 0.00001m);
    }

    [Fact]
    public void ParseXml_Nominal10Fixture_DividesValueByNominal()
    {
        var (rubPerUsd, rubPerEur) = CbrClient.ParseXml(CbrXmlNominal10);
        rubPerUsd.Should().Be(91.5m);  // 915 / 10
        rubPerEur.Should().Be(103.0m); // 1030 / 10
    }

    [Fact]
    public async Task FetchRatesAsync_DecodesCbrWindows1251Response()
    {
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes(CbrXml))
        };
        response.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml") { CharSet = "windows-1251" };
        var client = new CbrClient(new StubHttpClientFactory(response));

        var (rubPerUsd, rubPerEur) = await client.FetchRatesAsync(CancellationToken.None);

        rubPerUsd.Should().Be(91.5m);
        rubPerEur.Should().Be(103.0m);
    }

    // --- Refresher: fallback on cold start ---

    [Fact]
    public void Refresher_Constructor_PrePopulatesSnapshotWithFallback()
    {
        var snapshot = new CbrRateSnapshot();
        var settings = Options.Create(new ExchangeRateSettings
        {
            Fallback = new() { RubPerUsd = 95.0m, RubPerEur = 103.0m }
        });
        _ = new CbrRateRefresher(new FailingCbrClient(), snapshot, settings,
            NullLogger<CbrRateRefresher>.Instance);

        snapshot.HasRates.Should().BeTrue();
        var (rubPerUsd, rubPerEur) = snapshot.GetRates();
        rubPerUsd.Should().Be(95.0m);
        rubPerEur.Should().Be(103.0m);
    }

    // --- Refresher: success → snapshot updated ---

    [Fact]
    public async Task FetchWithRetry_WhenCbrSucceeds_UpdatesSnapshot()
    {
        var snapshot = new CbrRateSnapshot();
        var settings = Options.Create(new ExchangeRateSettings
        {
            Fallback = new() { RubPerUsd = 95.0m, RubPerEur = 103.0m }
        });
        var refresher = new CbrRateRefresher(new SucceedingCbrClient(91.5m, 103.0m), snapshot, settings,
            NullLogger<CbrRateRefresher>.Instance);

        await refresher.FetchWithRetryAsync(CancellationToken.None);

        var (rubPerUsd, rubPerEur) = snapshot.GetRates();
        rubPerUsd.Should().Be(91.5m);
        rubPerEur.Should().Be(103.0m);
    }

    // --- Refresher: error after success → last-known-good preserved ---

    [Fact]
    public async Task FetchWithRetry_WhenCbrFailsAfterSuccess_KeepsLastKnownGood()
    {
        var snapshot = new CbrRateSnapshot();
        snapshot.Update(88.0m, 99.0m); // simulate a previously successful fetch

        var settings = Options.Create(new ExchangeRateSettings
        {
            Fallback = new() { RubPerUsd = 95.0m, RubPerEur = 103.0m }
        });
        var refresher = new CbrRateRefresher(new FailingCbrClient(), snapshot, settings,
            NullLogger<CbrRateRefresher>.Instance);

        // Use a pre-cancelled token so retry backoffs abort immediately
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await refresher.FetchWithRetryAsync(cts.Token);

        var (rubPerUsd, rubPerEur) = snapshot.GetRates();
        rubPerUsd.Should().Be(88.0m);
        rubPerEur.Should().Be(99.0m);
    }

    // --- Conversion and formatting ---

    [Fact]
    public void ExchangeRateProvider_ConvertsUsdToRubAndEur()
    {
        var provider = CreateRateProvider(91.5m, 103.0m);

        provider.UsdTo(DisplayCurrency.Usd).Should().Be(1m);
        provider.UsdTo(DisplayCurrency.Rub).Should().Be(91.5m);
        provider.UsdTo(DisplayCurrency.Eur).Should().BeApproximately(91.5m / 103.0m, 0.00001m);
    }

    [Fact]
    public void ExchangeRateProvider_ConvertsDisplayAmountsToUsd()
    {
        var provider = CreateRateProvider(91.5m, 103.0m);

        provider.ToUsd(10m, DisplayCurrency.Usd).Should().Be(10m);
        provider.ToUsd(915m, DisplayCurrency.Rub).Should().Be(10m);
        provider.ToUsd(10m, DisplayCurrency.Eur).Should().BeApproximately(10m * 103.0m / 91.5m, 0.00001m);
    }

    [Theory]
    [InlineData(DisplayCurrency.Usd, "$10.00")]
    [InlineData(DisplayCurrency.Rub, "₽915.00")]
    [InlineData(DisplayCurrency.Eur, "€8.88")]
    public void CurrencyFormatter_FormatsConvertedAmount(DisplayCurrency currency, string expected)
    {
        var formatter = new CurrencyFormatter(CreateRateProvider(91.5m, 103.0m));

        formatter.Format(10m, currency).Should().Be(expected);
    }

    // --- Test doubles ---

    private static CbrExchangeRateProvider CreateRateProvider(decimal rubPerUsd, decimal rubPerEur)
    {
        var snapshot = new CbrRateSnapshot();
        snapshot.Update(rubPerUsd, rubPerEur);
        return new CbrExchangeRateProvider(snapshot);
    }

    private class SucceedingCbrClient : CbrClient
    {
        private readonly decimal _rubPerUsd;
        private readonly decimal _rubPerEur;

        public SucceedingCbrClient(decimal rubPerUsd, decimal rubPerEur)
            : base(new NullHttpClientFactory())
        {
            _rubPerUsd = rubPerUsd;
            _rubPerEur = rubPerEur;
        }

        public override Task<(decimal rubPerUsd, decimal rubPerEur)> FetchRatesAsync(CancellationToken ct)
            => Task.FromResult((_rubPerUsd, _rubPerEur));
    }

    private class FailingCbrClient : CbrClient
    {
        public FailingCbrClient() : base(new NullHttpClientFactory()) { }

        public override Task<(decimal rubPerUsd, decimal rubPerEur)> FetchRatesAsync(CancellationToken ct)
            => Task.FromException<(decimal, decimal)>(new HttpRequestException("CBR unavailable"));
    }

    private class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new NotSupportedException("HTTP not used in unit tests");
    }

    private sealed class StubHttpClientFactory(HttpResponseMessage response) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new StubHttpMessageHandler(response));
    }

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(response);
    }
}
