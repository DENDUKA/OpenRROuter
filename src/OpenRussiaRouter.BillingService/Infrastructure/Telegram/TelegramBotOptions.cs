namespace OpenRussiaRouter.BillingService.Infrastructure.Telegram;

public sealed class TelegramBotOptions
{
    public const string SectionName = "TelegramBot";

    public bool Enabled { get; set; }
    public string? Endpoint { get; set; } = "http://rawrter-tg-bot:8080/v1/events/first-api-request";
    public string? AuthToken { get; set; }
}
