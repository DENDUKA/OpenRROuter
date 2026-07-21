namespace OpenRussiaRouter.ContentService.Application.Exceptions;

/// <summary>
/// Thrown by the application layer when input fails validation. The gRPC layer catches it and
/// maps <see cref="ErrorCode"/> into the response's <c>error_code</c> field rather than letting
/// an RpcException escape — same convention as BillingService.
/// </summary>
public sealed class ContentValidationException(string errorCode, string message)
    : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}
