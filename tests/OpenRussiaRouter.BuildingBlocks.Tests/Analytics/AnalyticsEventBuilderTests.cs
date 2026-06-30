using FluentAssertions;
using OpenRussiaRouter.BuildingBlocks.Analytics;

namespace OpenRussiaRouter.BuildingBlocks.Tests.Analytics;

public sealed class AnalyticsEventBuilderTests
{
    private const string Salt = "test-salt";

    private static AnalyticsEventBuilder CreateBuilder(string environment = "test") =>
        new(new UserPseudonymizer(new ProductAnalyticsOptions { IdentitySalt = Salt }), environment);

    [Fact]
    public void BuildUserVerified_KeepsOnlyAllowedKeysAndAddsMetadata()
    {
        var builder = CreateBuilder();
        var raw = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["signup_method"] = "email",
            ["email"] = "leaked@example.com",
            ["prompt"] = "leaked prompt",
        };

        var evt = builder.Build(AnalyticsEvents.UserVerified, "user-1", raw);

        evt.EventName.Should().Be("user verified");
        evt.Props.Should().Contain(new KeyValuePair<string, object>("signup_method", "email"));
        evt.Props.Should().NotContainKey("email");
        evt.Props.Should().NotContainKey("prompt");
        evt.Props.Should().Contain(new KeyValuePair<string, object>("schema_version", "2"));
        evt.Props.Should().Contain(new KeyValuePair<string, object>("environment", "test"));
    }

    [Fact]
    public void BuildApiKeyCreated_HasOnlySchemaVersionAndEnvironment()
    {
        var builder = CreateBuilder();

        var evt = builder.Build(AnalyticsEvents.ApiKeyCreated, "user-1", new Dictionary<string, object>
        {
            ["api_key"] = "k-123",
            ["name"] = "production-key",
        });

        evt.Props.Keys.Should().BeEquivalentTo(new[] { "schema_version", "environment" });
        evt.Props.Should().NotContainKey("api_key");
        evt.Props.Should().NotContainKey("name");
    }

    [Fact]
    public void BuildApiRequestCompleted_AppliesBucketsAndIncludesEndpointAndStream()
    {
        var builder = CreateBuilder();
        var data = new ApiRequestAnalytics(
            Model: "gpt-4",
            StatusCode: 200,
            InputTokens: 500,
            OutputTokens: 1500,
            LatencyMs: 750,
            DedupKey: "usage-1",
            Endpoint: ApiEndpoint.ChatCompletions,
            Stream: true);

        var evt = builder.BuildApiRequestCompleted("user-1", data);

        evt.Props["model"].Should().Be("gpt-4");
        evt.Props["status_class"].Should().Be("2xx");
        evt.Props["input_tokens_bucket"].Should().Be("1-1K");
        evt.Props["output_tokens_bucket"].Should().Be("1K-10K");
        evt.Props["latency_bucket"].Should().Be("500ms-1s");
        evt.Props["endpoint"].Should().Be("chat_completions");
        evt.Props["stream"].Should().Be(true);

        // Raw values and the dedup key must never reach the props.
        evt.Props.Should().NotContainKey("input_tokens");
        evt.Props.Should().NotContainKey("output_tokens");
        evt.Props.Should().NotContainKey("latency_ms");
        evt.Props.Should().NotContainKey("status_code");
        evt.Props.Should().NotContainKey("dedup_key");
    }

    [Fact]
    public void BuildApiRequestCompleted_FailedRequest_AppliesStatusAndTokenBuckets()
    {
        var builder = CreateBuilder();
        var data = new ApiRequestAnalytics(
            Model: "tts-1",
            StatusCode: 500,
            InputTokens: 0,
            OutputTokens: 0,
            LatencyMs: 6_000,
            DedupKey: "usage-2",
            Endpoint: ApiEndpoint.Tts,
            Stream: false);

        var evt = builder.BuildApiRequestCompleted("user-1", data);

        evt.Props["status_class"].Should().Be("5xx");
        evt.Props["input_tokens_bucket"].Should().Be("0");
        evt.Props["latency_bucket"].Should().Be("5s+");
        evt.Props["endpoint"].Should().Be("tts");
        evt.Props["stream"].Should().Be(false);
    }

    [Fact]
    public void BuildApiRequestCompleted_WithoutEndpointOrStream_OmitsKeys()
    {
        var builder = CreateBuilder();
        var data = new ApiRequestAnalytics(
            Model: "gpt-4",
            StatusCode: 200,
            InputTokens: 500,
            OutputTokens: 1500,
            LatencyMs: 750,
            DedupKey: "usage-3",
            Endpoint: null,
            Stream: null);

        var evt = builder.BuildApiRequestCompleted("user-1", data);

        evt.Props.Should().NotContainKey("endpoint");
        evt.Props.Should().NotContainKey("stream");
    }

    [Fact]
    public void BuildPaymentCompleted_AppliesAmountBucket()
    {
        var builder = CreateBuilder();
        var data = new PaymentAnalytics(
            Provider: "yookassa",
            Currency: "RUB",
            Amount: 2500m,
            DedupKey: "payment-1");

        var evt = builder.BuildPaymentCompleted("user-1", data);

        evt.Props["provider"].Should().Be("yookassa");
        evt.Props["currency"].Should().Be("RUB");
        evt.Props["amount_bucket"].Should().Be("1K-10K");
        evt.Props.Should().NotContainKey("amount");
        evt.Props.Should().NotContainKey("dedup_key");
    }

    [Fact]
    public void Build_DeniedKeysFromCallerData_AreStripped()
    {
        var builder = CreateBuilder();
        var raw = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["signup_method"] = "email",
            ["email"] = "leak@example.com",
            ["token"] = "secret-token",
            ["api_key"] = "k-123",
            ["control_token"] = "ct-1",
            ["authorization"] = "Bearer abc",
            ["cookie"] = "session=xyz",
        };

        var evt = builder.Build(AnalyticsEvents.UserVerified, "user-1", raw);

        evt.Props.Should().NotContainKey("email");
        evt.Props.Should().NotContainKey("token");
        evt.Props.Should().NotContainKey("api_key");
        evt.Props.Should().NotContainKey("control_token");
        evt.Props.Should().NotContainKey("authorization");
        evt.Props.Should().NotContainKey("cookie");
    }

    [Fact]
    public void Build_DistinctIdIsPseudonymizedUserId()
    {
        var builder = CreateBuilder();

        var evt = builder.BuildApiKeyCreated("user-12345");

        evt.DistinctId.Should().NotContain("user-12345");
        evt.DistinctId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Build_ReturnsImmutableProperties()
    {
        var builder = CreateBuilder();

        var evt = builder.BuildApiKeyCreated("user-1");

        evt.Props.Should().NotBeAssignableTo<Dictionary<string, object>>();
    }

    [Fact]
    public void AllowedCallerKeys_CannotBeMutatedThroughPublishedContract()
    {
        AnalyticsEvents.TryGetAllowedCallerKeys(AnalyticsEvents.UserVerified, out var allowedKeys)
            .Should().BeTrue();

        allowedKeys.Should().NotBeAssignableTo<HashSet<string>>();
        allowedKeys.Should().BeEquivalentTo("signup_method");
    }

    [Fact]
    public void Build_UnknownEventName_Throws()
    {
        var builder = CreateBuilder();

        var act = () => builder.Build("totally unknown event", "user-1", new Dictionary<string, object>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*totally unknown event*");
    }

    [Fact]
    public void Build_EmptyUserId_Throws()
    {
        var builder = CreateBuilder();

        var act = () => builder.BuildApiKeyCreated(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_EnvironmentLabelPropagatesFromConfig()
    {
        var builder = CreateBuilder(environment: "staging");

        var evt = builder.BuildApiKeyCreated("user-1");

        evt.Props["environment"].Should().Be("staging");
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1-1K")]
    [InlineData(1_000, "1-1K")]
    [InlineData(1_001, "1K-10K")]
    [InlineData(10_000, "1K-10K")]
    [InlineData(10_001, "10K-100K")]
    [InlineData(100_000, "10K-100K")]
    [InlineData(100_001, "100K+")]
    public void AnalyticsBuckets_TokenBucket_ReturnsExpectedLabel(int tokens, string expected)
    {
        AnalyticsBuckets.TokenBucket(tokens).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1-1K")]
    [InlineData(1_000, "1-1K")]
    [InlineData(1_001, "1K-10K")]
    [InlineData(10_000, "1K-10K")]
    [InlineData(10_001, "10K+")]
    public void AnalyticsBuckets_AmountBucket_ReturnsExpectedLabel(decimal amount, string expected)
    {
        AnalyticsBuckets.AmountBucket(amount).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "0-100ms")]
    [InlineData(100, "0-100ms")]
    [InlineData(101, "100-500ms")]
    [InlineData(500, "100-500ms")]
    [InlineData(501, "500ms-1s")]
    [InlineData(1_000, "500ms-1s")]
    [InlineData(1_001, "1s-5s")]
    [InlineData(5_000, "1s-5s")]
    [InlineData(5_001, "5s+")]
    public void AnalyticsBuckets_LatencyBucket_ReturnsExpectedLabel(long latencyMs, string expected)
    {
        AnalyticsBuckets.LatencyBucket(latencyMs).Should().Be(expected);
    }

    [Theory]
    [InlineData(200, "2xx")]
    [InlineData(299, "2xx")]
    [InlineData(301, "3xx")]
    [InlineData(400, "4xx")]
    [InlineData(404, "4xx")]
    [InlineData(500, "5xx")]
    [InlineData(599, "5xx")]
    [InlineData(100, "other")]
    [InlineData(600, "other")]
    public void AnalyticsBuckets_StatusClass_ReturnsExpectedLabel(int statusCode, string expected)
    {
        AnalyticsBuckets.StatusClass(statusCode).Should().Be(expected);
    }
}
