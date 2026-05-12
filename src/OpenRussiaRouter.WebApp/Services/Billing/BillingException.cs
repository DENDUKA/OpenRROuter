namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed class BillingException(string ErrorCode) : Exception
{
    public string ErrorCode { get; } = ErrorCode;
}