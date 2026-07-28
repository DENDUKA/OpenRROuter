using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.WebApp.Email;

namespace OpenRussiaRouter.WebApp.Api;

/// <summary>
/// <c>POST /internal/alerts/grafana</c> — receives a Grafana Alerting webhook payload and emails
/// it through the existing Brevo HTTP path.
/// </summary>
/// <remarks>
/// This exists because the prod box blocks outbound SMTP (25/465/587/2525), so Grafana's native
/// SMTP contact point cannot deliver. The app already sends transactional mail over Brevo's HTTPS
/// API (<c>Email:Transport=brevoApi</c>); this relays alert notifications over the same working
/// path by enqueueing onto the shared <see cref="IEmailDispatchQueue"/>.
///
/// The endpoint is anonymous (Grafana can't do cookie auth) and only reachable on the compose
/// network, but it is gated by a shared token in the <c>X-Alert-Token</c> header so nothing else
/// on that network can send mail through it. If the token or recipient is unconfigured the endpoint
/// is disabled (503) rather than left open.
/// </remarks>
public static class AlertWebhookEndpoint
{
    /// <summary>Hard cap on webhook body size.</summary>
    public const int MaxBodyBytes = 256 * 1024;

    /// <summary>Header carrying the shared secret configured as <c>Alerting:WebhookToken</c>.</summary>
    public const string TokenHeader = "X-Alert-Token";

    /// <summary>Rate-limit policy name applied to this endpoint.</summary>
    public const string RateLimitPolicy = "alert-webhook";

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/internal/alerts/grafana", HandleAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy);

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        IConfiguration config,
        IEmailDispatchQueue queue,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("AlertWebhookEndpoint");

        var expectedToken = config["Alerting:WebhookToken"];
        var recipient = config["Alerting:EmailTo"];
        if (string.IsNullOrWhiteSpace(expectedToken) || string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogWarning("Alert webhook hit but Alerting:WebhookToken/EmailTo not configured — disabled.");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (!TokenMatches(ExtractToken(http.Request), expectedToken))
        {
            logger.LogWarning("Alert webhook rejected: bad or missing token.");
            return Results.Unauthorized();
        }

        if (http.Request.ContentLength is > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        string rawBody;
        try
        {
            using var reader = new StreamReader(
                new LimitedStream(http.Request.Body, MaxBodyBytes),
                leaveOpen: true);
            rawBody = await reader.ReadToEndAsync(ct);
        }
        catch (PayloadTooLargeException)
        {
            logger.LogWarning("Alert webhook rejected: streamed body exceeds {Max}.", MaxBodyBytes);
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (rawBody.Length == 0)
        {
            return Results.BadRequest(new { error = "empty body" });
        }

        var (subject, body) = BuildEmail(rawBody, logger);

        try
        {
            await queue.EnqueueAsync(new EmailMessage(recipient, subject, body), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to enqueue alert email.");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Results.Ok(new { status = "queued" });
    }

    /// <summary>
    /// Reads the shared token from <c>Authorization: Bearer &lt;token&gt;</c> (what Grafana's webhook
    /// contact point sends via authorization_scheme/credentials) or the <c>X-Alert-Token</c> header.
    /// </summary>
    private static string ExtractToken(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (auth.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            return auth[bearer.Length..].Trim();
        }

        return request.Headers[TokenHeader].ToString();
    }

    /// <summary>Constant-time token comparison so a timing side-channel can't leak the secret.</summary>
    private static bool TokenMatches(string provided, string expected)
    {
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Turns a Grafana webhook payload into (subject, text body). Parses defensively — any shape
    /// change or malformed JSON falls back to emailing the truncated raw body so an alert is never
    /// silently dropped.
    /// </summary>
    private static (string Subject, string Body) BuildEmail(string rawBody, ILogger logger)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "unknown" : "unknown";
            var alerts = root.TryGetProperty("alerts", out var a) && a.ValueKind == JsonValueKind.Array
                ? a
                : default;

            var count = alerts.ValueKind == JsonValueKind.Array ? alerts.GetArrayLength() : 0;
            var firstName = "alert";
            var sb = new StringBuilder();
            sb.Append("Status: ").Append(status).Append('\n');
            sb.Append("Alerts: ").Append(count).Append("\n\n");

            if (alerts.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var alert in alerts.EnumerateArray())
                {
                    var name = GetNested(alert, "labels", "alertname") ?? "alert";
                    if (i == 0) firstName = name;

                    // Prefer the human-authored summary; fall back to the rule name so a line is
                    // never blank. The raw Grafana valueString ("[ var='A' … value=3 ]") is
                    // deliberately dropped — it's internal expression state, not information.
                    var summary = GetNested(alert, "annotations", "summary");
                    sb.Append("• ").Append(string.IsNullOrWhiteSpace(summary) ? name : summary).Append('\n');

                    var description = GetNested(alert, "annotations", "description");
                    if (!string.IsNullOrWhiteSpace(description)) sb.Append("    ").Append(description).Append('\n');

                    // The identifying labels of this specific series (provider/account/reason),
                    // so an alert that fires per connection actually names the connection.
                    var context = FormatLabels(alert);
                    if (!string.IsNullOrWhiteSpace(context)) sb.Append("    ").Append(context).Append('\n');

                    sb.Append('\n');
                    i++;
                }
            }

            var subject = $"[ORR {status.ToUpperInvariant()}] {count} alert(s): {firstName}";
            return (subject, sb.ToString());
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Alert webhook payload was not valid JSON — emailing raw body.");
            var truncated = rawBody.Length > 4000 ? rawBody[..4000] + "…(truncated)" : rawBody;
            return ("[ORR alert] (unparsed payload)", truncated);
        }
    }

    /// <summary>
    /// Renders the alert's identifying labels as <c>k=v, k=v</c>, keeping only the domain labels
    /// (provider, account_name, reason, model_id, limit_type, …) and dropping Grafana's internal
    /// bookkeeping (<c>alertname</c>, <c>grafana_folder</c>, <c>severity</c>, anything <c>__…__</c>)
    /// and the high-cardinality id labels (connection_id, node_id) that add noise, not meaning.
    /// </summary>
    private static string FormatLabels(JsonElement alert)
    {
        if (alert.ValueKind != JsonValueKind.Object
            || !alert.TryGetProperty("labels", out var labels)
            || labels.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var skip = new HashSet<string>(StringComparer.Ordinal)
        {
            "alertname", "grafana_folder", "severity", "connection_id", "node_id",
        };

        var parts = new List<string>();
        foreach (var label in labels.EnumerateObject())
        {
            if (label.Name.StartsWith("__", StringComparison.Ordinal) || skip.Contains(label.Name))
            {
                continue;
            }

            if (label.Value.ValueKind == JsonValueKind.String)
            {
                var v = label.Value.GetString();
                if (!string.IsNullOrWhiteSpace(v))
                {
                    parts.Add($"{label.Name}={v}");
                }
            }
        }

        return string.Join(", ", parts);
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string? GetNested(JsonElement el, string obj, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(obj, out var inner) && inner.ValueKind == JsonValueKind.Object
            ? GetString(inner, prop)
            : null;
}
