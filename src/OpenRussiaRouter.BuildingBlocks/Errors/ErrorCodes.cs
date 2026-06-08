namespace OpenRussiaRouter.BuildingBlocks.Errors;

public static class ErrorCodes
{
    public const string AuthenticationRequired = "authentication_required";
    public const string InvalidApiKey = "invalid_api_key";
    public const string InsufficientBalance = "insufficient_balance";
    public const string ModelUnavailable = "model_unavailable";
    public const string RouterUnavailable = "router_unavailable";
    public const string ValidationFailed = "validation_failed";
    public const string UnsupportedReasoning = "unsupported_parameter";
    public const string UnsupportedReasoningEffort = "unsupported_reasoning_effort";
    public const string ConflictingParameters = "conflicting_parameters";
    public const string InternalError = "internal_error";
    public const string PaymentNotFound = "payment_not_found";
    public const string PaymentAlreadyProcessed = "payment_already_processed";
    public const string PaymentAmountMismatch = "payment_amount_mismatch";
    public const string PaymentInvalidTransition = "payment_invalid_transition";
    public const string PaymentAmountOutOfRange = "payment_amount_out_of_range";
    public const string UnsupportedCurrency = "unsupported_currency";
    public const string FxQuoteUnavailable = "fx_quote_unavailable";
    public const string PaymentProviderUnavailable = "payment_provider_unavailable";
}

