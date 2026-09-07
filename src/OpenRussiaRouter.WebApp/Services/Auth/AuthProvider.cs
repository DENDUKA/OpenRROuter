using OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using OpenRussiaRouter.Contracts.Auth;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;

namespace OpenRussiaRouter.WebApp.Services.Auth;

public class AuthProvider(AuthGrpc.AuthServiceClient client)
{
    public async Task<LoginResponse> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var response = await client.LoginAsync(new LoginRequest { Email = email, Password = password }, cancellationToken: cancellationToken);
        if (!response.Succeeded)
        {
            throw new AuthException(response.ErrorCode);
        }
        return new LoginResponse(response.SessionToken, response.UserId, response.Role, email);
    }

    public async Task<RegistrationResult> RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var response = await client.RegisterUserAsync(new RegisterUserRequest { Email = email, Password = password }, cancellationToken: cancellationToken);
        if (!response.Succeeded)
        {
            throw new AuthException(response.ErrorCode);
        }
        return new RegistrationResult(response.UserId, response.Email, response.VerificationCode);
    }

    public virtual async Task<LoginResponse> VerifyEmailAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        var response = await client.VerifyEmailAsync(new VerifyEmailRequest { Email = email, Code = code }, cancellationToken: cancellationToken);
        if (!response.Succeeded)
        {
            throw new AuthException(response.ErrorCode);
        }
        return new LoginResponse(response.SessionToken, response.UserId, response.Role, email);
    }

    /// <summary>Re-issues a verification code for a pending signup; returns the raw code to email.</summary>
    public async Task<string> ResendVerificationAsync(string email, CancellationToken cancellationToken = default)
    {
        var response = await client.ResendVerificationAsync(new ResendVerificationRequest { Email = email }, cancellationToken: cancellationToken);
        if (!response.Succeeded)
        {
            throw new AuthException(response.ErrorCode);
        }
        return response.VerificationCode;
    }

    /// <summary>Starts a password reset; returns the normalized email and the raw 6-digit code to send by mail.</summary>
    public async Task<(string Email, string Code)> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        var response = await client.RequestPasswordResetAsync(new RequestPasswordResetRequest { Email = email }, cancellationToken: cancellationToken);
        if (!response.Succeeded)
        {
            throw new AuthException(response.ErrorCode);
        }
        return (response.Email, response.ResetCode);
    }

    public async Task ResetPasswordAsync(string email, string code, string newPassword, CancellationToken cancellationToken = default)
    {
        var response = await client.ResetPasswordAsync(new ResetPasswordRequest { Email = email, Code = code, NewPassword = newPassword }, cancellationToken: cancellationToken);
        if (!response.Succeeded)
        {
            throw new AuthException(response.ErrorCode);
        }
    }

    public async Task ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var response = await client.ChangePasswordAsync(new ChangePasswordRequest { UserId = userId, CurrentPassword = currentPassword, NewPassword = newPassword }, cancellationToken: cancellationToken);
        if (!response.Succeeded)
        {
            throw new AuthException(response.ErrorCode);
        }
    }

    public virtual async Task<LoginResponse?> LoginOrRegisterWithGoogleAsync(string googleSub, string email, CancellationToken cancellationToken = default)
    {
        var response = await client.LoginOrRegisterWithGoogleAsync(
            new LoginWithGoogleRequest { GoogleSub = googleSub, Email = email },
            cancellationToken: cancellationToken);
        if (!response.Succeeded)
            return null;
        return new LoginResponse(response.SessionToken, response.UserId, response.Role, email, response.IsNewUser);
    }

    public virtual async Task<LoginResponse?> LoginOrRegisterWithExternalProviderAsync(string provider, string providerSub, string email, CancellationToken cancellationToken = default)
    {
        var response = await client.LoginOrRegisterWithExternalProviderAsync(
            new LoginWithExternalProviderRequest { Provider = provider, ProviderSub = providerSub, Email = email },
            cancellationToken: cancellationToken);
        if (!response.Succeeded)
            return null;
        return new LoginResponse(response.SessionToken, response.UserId, response.Role, email, response.IsNewUser);
    }

    public async Task<InviteCompletionResponse> CompleteInviteAsync(string rawToken, string password, CancellationToken cancellationToken = default)
    {
        var response = await client.CompleteInviteAsync(new CompleteInviteRequest { RawToken = rawToken, Password = password }, cancellationToken: cancellationToken);
        if (!response.IsCompleted)
        {
            throw new AuthException(response.ErrorCode);
        }
        return new InviteCompletionResponse(response.UserId, response.Email, response.Role);
    }

    public async Task<CreateApiKeyResponse> CreateApiKeyAsync(string userId, string name, CancellationToken cancellationToken = default)
    {
        var response = await client.CreateApiKeyAsync(new CreateApiKeyRequest { UserId = userId, Name = name }, cancellationToken: cancellationToken);
        if (!response.IsCreated)
        {
            throw new AuthException(response.ErrorCode);
        }
        return new CreateApiKeyResponse(response.ApiKeyId, response.RawKey, response.Name);
    }

    public virtual async Task<IReadOnlyList<ApiKeySummary>> ListApiKeysAsync(string userId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var response = await client.ListApiKeysAsync(new ListApiKeysRequest { UserId = userId, IncludeDeleted = includeDeleted }, cancellationToken: cancellationToken);
        return response.Keys.Select(k => new ApiKeySummary(
            k.Id,
            k.Name,
            k.Prefix,
            DateTimeOffset.FromUnixTimeSeconds(k.CreatedAtUnixTimeSeconds),
            k.LastUsedAtUnixTimeSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(k.LastUsedAtUnixTimeSeconds) : null,
            k.DisabledAtUnixTimeSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(k.DisabledAtUnixTimeSeconds) : null,
            k.IsDeleted,
            string.IsNullOrEmpty(k.RawKey) ? null : k.RawKey
        )).ToList();
    }

    public async Task RenameApiKeyAsync(string userId, string apiKeyId, string name, CancellationToken cancellationToken = default)
    {
        var response = await client.RenameApiKeyAsync(new RenameApiKeyRequest { UserId = userId, ApiKeyId = apiKeyId, Name = name }, cancellationToken: cancellationToken);
        if (!response.IsRenamed)
        {
            throw new AuthException(response.ErrorCode);
        }
    }

    public async Task DisableApiKeyAsync(string userId, string apiKeyId, CancellationToken cancellationToken = default)
    {
        var response = await client.DisableApiKeyAsync(new DisableApiKeyRequest { UserId = userId, ApiKeyId = apiKeyId }, cancellationToken: cancellationToken);
        if (!response.IsDisabled)
        {
            throw new AuthException(response.ErrorCode);
        }
    }

    public async Task EnableApiKeyAsync(string userId, string apiKeyId, CancellationToken cancellationToken = default)
    {
        var response = await client.EnableApiKeyAsync(new EnableApiKeyRequest { UserId = userId, ApiKeyId = apiKeyId }, cancellationToken: cancellationToken);
        if (!response.IsEnabled)
        {
            throw new AuthException(response.ErrorCode);
        }
    }

    public async Task DeleteApiKeyAsync(string userId, string apiKeyId, CancellationToken cancellationToken = default)
    {
        var response = await client.DeleteApiKeyAsync(new DeleteApiKeyRequest { UserId = userId, ApiKeyId = apiKeyId }, cancellationToken: cancellationToken);
        if (!response.IsDeleted)
        {
            throw new AuthException(response.ErrorCode);
        }
    }

    public async Task<string?> GetFirstActiveApiKeyRawAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await client.GetFirstActiveApiKeyRawAsync(
            new GetFirstActiveApiKeyRawRequest { UserId = userId }, cancellationToken: cancellationToken);
        return string.IsNullOrEmpty(response.RawKey) ? null : response.RawKey;
    }

    public async Task<IReadOnlyList<UserInfo>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListUsersAsync(new ListUsersRequest(), cancellationToken: cancellationToken);
        return response.Users.Select(u => new UserInfo(
            u.Id,
            u.Email,
            u.Role,
            u.Status,
            DateTimeOffset.FromUnixTimeSeconds(u.CreatedAtUnixTimeSeconds),
            u.HasDisplayName ? u.DisplayName : null,
            u.MaxApiKeys,
            u.HasDisplayCurrency ? u.DisplayCurrency : "RUB")).ToList();
    }

    public async Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await client.GetUserAsync(new GetUserRequest { UserId = userId }, cancellationToken: cancellationToken);
        if (response.User == null) return null;
        var u = response.User;
        return new UserInfo(
            u.Id, u.Email, u.Role, u.Status,
            DateTimeOffset.FromUnixTimeSeconds(u.CreatedAtUnixTimeSeconds),
            u.HasDisplayName ? u.DisplayName : null,
            u.MaxApiKeys,
            u.HasDisplayCurrency ? u.DisplayCurrency : "RUB");
    }

    public async Task UpdateUserAsync(string userId, string? displayName, int maxApiKeys, string? displayCurrency = null, CancellationToken cancellationToken = default)
    {
        var request = new UpdateUserRequest
        {
            UserId = userId,
            MaxApiKeys = maxApiKeys
        };
        if (displayName != null) request.DisplayName = displayName;
        if (displayCurrency != null) request.DisplayCurrency = displayCurrency;

        var response = await client.UpdateUserAsync(request, cancellationToken: cancellationToken);
        if (!response.Succeeded) throw new AuthException(response.ErrorCode);
    }

    public async Task<(string RawToken, string Email, string Role)> CreateInviteAsync(string email, string role, string adminId, CancellationToken cancellationToken = default)
    {
        var response = await client.CreateInviteAsync(new CreateInviteRequest
        {
            Email = email,
            Role = role,
            CreatedByAdminId = adminId,
            ExpiresAtUnixTimeSeconds = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds()
        }, cancellationToken: cancellationToken);
        if (!response.IsCreated)
        {
            throw new AuthException(response.ErrorCode);
        }
        return (response.RawToken, response.Email, response.Role);
    }
}
