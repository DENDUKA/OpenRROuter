using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.ControlPlane.Application.Services;

namespace OpenRussiaRouter.ControlPlane.Application.Metrics;

public class ProviderQuotaCollectorOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
}

public class ProviderQuotaCollectorWorker : BackgroundService
{
    private readonly RouterControlPlaneService _controlPlane;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProviderQuotaCollectorWorker> _logger;
    private readonly ProviderQuotaCollectorOptions _options;
    private readonly IOptionsMonitor<RouterHealthCheckOptions> _healthCheckOptions;
    private readonly Meter _meter;
    private readonly List<QuotaMeasurement> _latestMeasurements = new();
    private readonly object _measurementsLock = new();

    public ProviderQuotaCollectorWorker(
        RouterControlPlaneService controlPlane,
        HttpClient httpClient,
        IOptions<ProviderQuotaCollectorOptions> options,
        IOptionsMonitor<RouterHealthCheckOptions> healthCheckOptions,
        ILogger<ProviderQuotaCollectorWorker> logger)
    {
        _controlPlane = controlPlane;
        _httpClient = httpClient;
        _options = options.Value;
        _healthCheckOptions = healthCheckOptions;
        _logger = logger;
        _httpClient.Timeout = _options.Timeout;

        _meter = new Meter("OpenRussiaRouter");
        _meter.CreateObservableGauge("orr_provider_quota", GetQuotaMeasurements, unit: "tokens", description: "Remaining quota for providers");
    }

    private IEnumerable<Measurement<double>> GetQuotaMeasurements()
    {
        lock (_measurementsLock)
        {
            return _latestMeasurements.Select(m => new Measurement<double>(m.Value, new KeyValuePair<string, object?>[]
            {
                new("node_id", m.NodeId),
                new("provider", m.Provider),
                new("connection_id", m.ConnectionId)
            })).ToList();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Provider quota collector is disabled.");
            return;
        }

        _logger.LogInformation("Provider quota collector started. Interval: {Interval}", _options.Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectQuotasAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while collecting provider quotas.");
            }

            await Task.Delay(_options.Interval, stoppingToken);
        }
    }

    private async Task CollectQuotasAsync(CancellationToken cancellationToken)
    {
        var nodes = await _controlPlane.ListNodesForHealthChecksAsync(cancellationToken);
        var newMeasurements = new List<QuotaMeasurement>();

        foreach (var node in nodes.Where(n => n.Enabled))
        {
            try
            {
                var healthOpts = _healthCheckOptions.CurrentValue;
                var requestUrl = new Uri(node.EndpointUrl, "/api/integrations/providers/quota");

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                
                if (healthOpts.NodeTokens != null && healthOpts.NodeTokens.TryGetValue(node.Id, out var token) && !string.IsNullOrEmpty(healthOpts.NodeTokenHeader))
                {
                    request.Headers.Add(healthOpts.NodeTokenHeader, token);
                }

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var jsonDocument = JsonDocument.Parse(content);
                    
                    if (jsonDocument.RootElement.TryGetProperty("providers", out var providersArray))
                    {
                        foreach (var providerElement in providersArray.EnumerateArray())
                        {
                            if (providerElement.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind != JsonValueKind.Null)
                            {
                                if (usageElement.TryGetProperty("remaining", out var remainingElement))
                                {
                                    var remaining = remainingElement.GetDouble();
                                    var provider = providerElement.GetProperty("provider").GetString() ?? "unknown";
                                    var connectionId = providerElement.GetProperty("connectionId").GetString() ?? "unknown";
                                    
                                    newMeasurements.Add(new QuotaMeasurement(node.Id, provider, connectionId, remaining));
                                }
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to collect quotas from node {NodeId}. Status Code: {StatusCode}", node.Id, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception while collecting quotas from node {NodeId}", node.Id);
            }
        }

        lock (_measurementsLock)
        {
            _latestMeasurements.Clear();
            _latestMeasurements.AddRange(newMeasurements);
        }
    }

    private record QuotaMeasurement(string NodeId, string Provider, string ConnectionId, double Value);
}
