using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenRussiaRouter.CollectorService.Application.Collection;
using OpenRussiaRouter.CollectorService.Application.NodeAdmin;
using OpenRussiaRouter.CollectorService.Application.Quota;

namespace OpenRussiaRouter.CollectorService.Tests.Collection;

/// <summary>
/// Формы <c>usage</c> взяты дословно из живого ответа ноды 9R
/// (<c>GET /api/integrations/providers/quota</c>): из пяти подключений реальную квоту
/// отдавало только одно, остальные возвращали 200 с текстом ошибки внутри. Раньше такие
/// подключения молча исчезали из метрик — эти тесты фиксируют, что теперь они видимы.
/// </summary>
public class NodeQuotaCollectorTests
{
    private static NodeQuotaCollector Build(INodeAdminClient client) =>
        new(client, Options.Create(new CollectorOptions()).ToMonitor(), NullLogger<NodeQuotaCollector>.Instance);

    private static CollectorNode Node() => new("n1", new Uri("http://n1/"), "t", true, "Healthy", true);

    private static INodeAdminClient ClientReturning(params (string Provider, string Name, string UsageJson)[] providers)
    {
        var mock = new Mock<INodeAdminClient>();
        mock.Setup(c => c.GetProviderQuotaAsync(It.IsAny<Uri>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeProviderQuotaResponse
            {
                Providers = providers.Select((p, i) => new NodeProviderQuotaEntry
                {
                    ConnectionId = $"c{i}",
                    Provider = p.Provider,
                    Name = p.Name,
                    IsActive = true,
                    QuotaSupported = true,
                    Usage = JsonDocument.Parse(p.UsageJson).RootElement,
                }).ToList()
            });
        return mock.Object;
    }

    [Fact]
    public async Task CollectAsync_ReadsNestedQuota_AndMarksConnectionReporting()
    {
        var client = ClientReturning(("glm", "glm-light",
            """{"plan":"Lite","quotas":{"session":{"used":0,"total":100,"remaining":100,"remainingPercentage":100,"unlimited":false}}}"""));

        var result = await Build(client).CollectAsync(Node(), CancellationToken.None);

        result.Measurements.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { QuotaName = "session", Remaining = 100d, Percent = 100d });
        result.Statuses.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Provider = "glm", Reporting = true, Reason = QuotaUnavailableReasons.Ok });
    }

    [Fact]
    public async Task CollectAsync_EmptyQuotasObject_YieldsNoMeasurementButAVisibleStatus()
    {
        // Antigravity с протухшей авторизацией: HTTP 200, quotas пуст, причина в message.
        var client = ClientReturning(("antigravity", "someone@gmail.com",
            """{"message":"Antigravity quota API authentication expired. Chat may still work.","quotas":{}}"""));

        var result = await Build(client).CollectAsync(Node(), CancellationToken.None);

        result.Measurements.Should().BeEmpty();
        result.Statuses.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Provider = "antigravity",
                AccountName = "someone@gmail.com",
                Reporting = false,
                Reason = QuotaUnavailableReasons.EmptyQuotas,
            });
    }

    [Fact]
    public async Task CollectAsync_UsageWithoutQuotaNumbers_IsReportedAsNoQuotaFields()
    {
        // gemini-cli / minimax: ни quotas, ни числовых полей — только сообщение об ошибке.
        var client = ClientReturning(("gemini-cli", "someone@gmail.com",
            """{"plan":"Free","message":"Gemini CLI quota error (401)."}"""));

        var result = await Build(client).CollectAsync(Node(), CancellationToken.None);

        result.Measurements.Should().BeEmpty();
        result.Statuses.Should().ContainSingle()
            .Which.Reason.Should().Be(QuotaUnavailableReasons.NoQuotaFields);
    }

    [Fact]
    public async Task CollectAsync_MixedConnections_KeepsWorkingOnesAndStillFlagsBrokenOnes()
    {
        var client = ClientReturning(
            ("glm", "glm-light", """{"quotas":{"session":{"remaining":40,"total":100,"remainingPercentage":40}}}"""),
            ("antigravity", "a@x.com", """{"message":"expired","quotas":{}}"""),
            ("minimax", "minimax-light", """{"message":"MiniMax API key invalid or inactive."}"""));

        var result = await Build(client).CollectAsync(Node(), CancellationToken.None);

        result.Measurements.Should().ContainSingle().Which.Percent.Should().Be(40);
        result.Statuses.Should().HaveCount(3);
        result.Statuses.Count(s => s.Reporting).Should().Be(1);
        result.Statuses.Where(s => !s.Reporting).Select(s => s.Reason)
            .Should().BeEquivalentTo([QuotaUnavailableReasons.EmptyQuotas, QuotaUnavailableReasons.NoQuotaFields]);
    }

    [Fact]
    public async Task CollectAsync_NodeUnreachable_StillEmitsAStatusRow()
    {
        var mock = new Mock<INodeAdminClient>();
        mock.Setup(c => c.GetProviderQuotaAsync(It.IsAny<Uri>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var result = await Build(mock.Object).CollectAsync(Node(), CancellationToken.None);

        result.Measurements.Should().BeEmpty();
        result.Statuses.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                NodeId = "n1",
                Reporting = false,
                Reason = QuotaUnavailableReasons.NodeUnreachable,
            });
    }
}
