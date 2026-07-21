namespace OpenRussiaRouter.WebApp.Services.Content;

/// <summary>
/// Raised when ContentService rejects a mutating call. <see cref="ErrorCode"/> is the machine
/// code from the response; pages map it to a user-facing message rather than printing it raw.
/// </summary>
public sealed class ContentException(string errorCode)
    : Exception($"Content operation failed: {errorCode}")
{
    public string ErrorCode { get; } = errorCode;
}
