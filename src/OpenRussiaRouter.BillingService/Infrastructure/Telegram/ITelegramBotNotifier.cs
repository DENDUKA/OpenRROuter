namespace OpenRussiaRouter.BillingService.Infrastructure.Telegram;

public interface ITelegramBotNotifier
{
    void NotifyFirstApiRequest(string userId, string modelId);
}
