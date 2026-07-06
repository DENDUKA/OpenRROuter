using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.WebApp.Email;

/// <summary>
/// <see cref="IEmailSender"/> that sends through Brevo's transactional HTTP API
/// (<c>POST https://api.brevo.com/v3/smtp/email</c>) instead of SMTP. Used on hosts that block
/// outbound SMTP ports (25/465/587/2525) — only HTTPS/443 is required. Selected via
/// <c>Email:Transport=brevoApi</c>; the API key is read from configuration
/// (<c>Email:BrevoApiKey</c>) so it never sits in a serialized settings object.
/// </summary>
public sealed class BrevoApiEmailSender : IEmailSender
{
    /// <summary>Named <see cref="HttpClient"/> registered in <c>AddEmailInfrastructure</c>.</summary>
    public const string HttpClientName = "BrevoApi";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmailSettings _settings;
    private readonly string? _apiKey;
    private readonly ILogger<BrevoApiEmailSender> _logger;

    public BrevoApiEmailSender(
        IHttpClientFactory httpClientFactory,
        IOptions<EmailSettings> settings,
        IConfiguration configuration,
        ILogger<BrevoApiEmailSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _apiKey = configuration["Email:BrevoApiKey"];
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogError("Brevo API key not configured (Email:BrevoApiKey); cannot send email.");
            throw new InvalidOperationException("Brevo API key is not configured.");
        }

        var payload = new SendEmailRequest
        {
            Sender = new Contact { Name = _settings.SenderName, Email = _settings.SenderAddress },
            To = new[] { new Contact { Email = message.To } },
            Subject = message.Subject,
            TextContent = message.TextBody,
            ReplyTo = string.IsNullOrWhiteSpace(message.ReplyTo)
                ? null
                : new Contact { Email = message.ReplyTo },
            Attachment = message.Attachments is { Count: > 0 }
                ? message.Attachments
                    .Select(a => new Attachment { Name = a.FileName, Content = Convert.ToBase64String(a.Content) })
                    .ToArray()
                : null,
        };

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/smtp/email")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.TryAddWithoutValidation("api-key", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "Brevo API send failed ({Status}) to {To} '{Subject}': {Body}",
                    (int)response.StatusCode, message.To, message.Subject, body);
                throw new InvalidOperationException($"Brevo API returned HTTP {(int)response.StatusCode}.");
            }

            _logger.LogInformation("Email sent via Brevo API to {To} '{Subject}'", message.To, message.Subject);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to send email via Brevo API to {To} '{Subject}'", message.To, message.Subject);
            throw;
        }
    }

    private sealed class SendEmailRequest
    {
        [JsonPropertyName("sender")] public Contact Sender { get; set; } = null!;
        [JsonPropertyName("to")] public Contact[] To { get; set; } = null!;
        [JsonPropertyName("subject")] public string Subject { get; set; } = null!;
        [JsonPropertyName("textContent")] public string TextContent { get; set; } = null!;

        [JsonPropertyName("replyTo")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Contact? ReplyTo { get; set; }

        [JsonPropertyName("attachment")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Attachment[]? Attachment { get; set; }
    }

    private sealed class Contact
    {
        [JsonPropertyName("email")] public string Email { get; set; } = null!;

        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }
    }

    private sealed class Attachment
    {
        [JsonPropertyName("name")] public string Name { get; set; } = null!;
        [JsonPropertyName("content")] public string Content { get; set; } = null!;
    }
}
