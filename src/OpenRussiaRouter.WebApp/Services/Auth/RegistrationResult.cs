namespace OpenRussiaRouter.WebApp.Services.Auth;

/// <summary>
/// Result of a self-serve signup. <see cref="VerificationCode"/> is the raw
/// 6-digit code the WebApp must email to the user (it is never persisted in clear).
/// </summary>
public sealed record RegistrationResult(string UserId, string Email, string VerificationCode);
