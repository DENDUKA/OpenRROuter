namespace OpenRussiaRouter.WebApp.Services.Auth;

public sealed class AuthException(string ErrorCode) : Exception
{
    public string ErrorCode { get; } = ErrorCode;
}