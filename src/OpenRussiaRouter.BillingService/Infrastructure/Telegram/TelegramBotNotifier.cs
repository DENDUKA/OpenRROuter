using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;

namespace OpenRussiaRouter.BillingService.Infrastructure.Telegram;

public sealed class TelegramBotNotifier : ITelegramBotNotifier
{
    private readonly AuthService.AuthServiceClient? _authClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TelegramBotOptions _options;
    private readonly ILogger<TelegramBotNotifier> _logger;

    public TelegramBotNotifier(
        IHttpClientFactory httpClientFactory,
        IOptions<TelegramBotOptions> options,
        ILogger<TelegramBotNotifier> logger,
        AuthService.AuthServiceClient? authClient = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _authClient = authClient;
    }

    public void NotifyFirstApiRequest(string userId, string modelId)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.Endpoint) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                string email = userId;
                if (_authClient is not null)
                {
                    try
                    {
                        var userRes = await _authClient.GetUserAsync(new GetUserRequest { UserId = userId });
                        if (!string.IsNullOrEmpty(userRes?.User?.Email))
                        {
                            email = userRes.User.Email;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resolve email from AuthService for user {UserId}", userId);
                    }
                }

                using var client = _httpClientFactory.CreateClient("TelegramBot");
                client.Timeout = TimeSpan.FromSeconds(10);

                var payload = new
                {
                    id = $"first-req-{userId}",
                    type = "first_api_request",
                    occurredAt = DateTimeOffset.UtcNow.ToString("O"),
                    payload = new
                    {
                        email,
                        model = modelId
                    }
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AuthToken);

                using var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Telegram bot responded with status {StatusCode} for first api request of {UserId}",
                        response.StatusCode, userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send first_api_request event to Telegram bot for user {UserId}", userId);
            }
        });
    }
}
