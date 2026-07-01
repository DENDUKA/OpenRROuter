using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRussiaRouter.ApiGateway.Billing;
using OpenRussiaRouter.Contracts.Grpc.Billing.V1;
using BillingGrpc = OpenRussiaRouter.Contracts.Grpc.Billing.V1.BillingService;

namespace OpenRussiaRouter.ApiGateway.Tests.Billing;

/// <summary>
/// Guards the GatewayUsageEvent → RecordUsageRequest wire mapping. The integration
/// tests stub IBillingProvider and therefore capture the GatewayUsageEvent *before*
/// this serialization, so they cannot catch a dropped proto field (e.g. endpoint/stream).
/// </summary>
public sealed class GrpcBillingProviderTests
{
    [Theory]
    [InlineData("chat_completions", true)]
    [InlineData("chat_completions", false)]
    [InlineData("tts", false)]
    public async Task RecordUsageAsync_ForwardsEndpointAndStreamOverTheWire(string endpoint, bool stream)
    {
        var client = new CapturingBillingClient();
        var provider = new GrpcBillingProvider(client, NullLogger<GrpcBillingProvider>.Instance);
        var usageEvent = CreateUsageEvent(endpoint: endpoint, stream: stream);

        var accepted = await provider.RecordUsageAsync(usageEvent, CancellationToken.None);

        accepted.Should().BeTrue();
        client.Captured.Should().NotBeNull();
        client.Captured!.Endpoint.Should().Be(endpoint);
        client.Captured.Stream.Should().Be(stream);
    }

    [Fact]
    public async Task RecordUsageAsync_NullEndpointAndStream_SerializeToProtoDefaults()
    {
        var client = new CapturingBillingClient();
        var provider = new GrpcBillingProvider(client, NullLogger<GrpcBillingProvider>.Instance);
        var usageEvent = CreateUsageEvent(endpoint: null, stream: null);

        await provider.RecordUsageAsync(usageEvent, CancellationToken.None);

        client.Captured.Should().NotBeNull();
        client.Captured!.Endpoint.Should().BeEmpty();
        client.Captured.Stream.Should().BeFalse();
    }

    private static GatewayUsageEvent CreateUsageEvent(string? endpoint, bool? stream)
    {
        var now = DateTimeOffset.UtcNow;
        return new GatewayUsageEvent(
            "req-1", "user-1", "key-1", "sk-or-test", "gpt-4", "node-1",
            new OpenRussiaRouter.BuildingBlocks.Usage.TokenUsage(10, 5, 0), "completed",
            now.AddSeconds(-1), now,
            ProviderName: "prov", ProviderAccount: "acct",
            Endpoint: endpoint, Stream: stream);
    }

    private sealed class CapturingBillingClient : BillingGrpc.BillingServiceClient
    {
        public RecordUsageRequest? Captured { get; private set; }

        public override AsyncUnaryCall<RecordUsageResponse> RecordUsageAsync(
            RecordUsageRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            Captured = request;
            var response = new RecordUsageResponse { Accepted = true, RequestId = request.RequestId };
            return new AsyncUnaryCall<RecordUsageResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }
    }
}
