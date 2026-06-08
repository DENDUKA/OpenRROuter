namespace OpenRussiaRouter.BillingService.Domain.Enums;

public enum PaymentStatus
{
    Created,
    Paid,
    Canceled,
    Expired,
    Refunded,
    ChargedBack
}
