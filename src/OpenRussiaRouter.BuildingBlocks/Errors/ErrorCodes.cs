namespace OpenRussiaRouter.BuildingBlocks.Errors;

/// <summary>
/// Shared error codes used across Gateway, Auth, Billing and Control Plane.
/// </summary>
public static class ErrorCodes
{
    public const string AuthenticationRequired = "authentication_required";
    public const string InvalidApiKey = "invalid_api_key";
    public const string InsufficientBalance = "insufficient_balance";
    public const string ModelUnavailable = "model_unavailable";
    public const string RouterUnavailable = "router_unavailable";
    public const string ValidationFailed = "validation_failed";
    public const string InternalError = "internal_error";
}

