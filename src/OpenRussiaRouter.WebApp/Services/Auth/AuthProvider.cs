using OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;

namespace OpenRussiaRouter.WebApp.Services.Auth;

public sealed class AuthProvider(AuthGrpc.AuthServiceClient client)
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

    public async Task<LoginResponse> VerifyEmailAsync(string email, string code, CancellationToken cancellationToken = default)
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

    public async Task<LoginResponse?> LoginOrRegisterWithGoogleAsync(string googleSub, string email, CancellationToken cancellationToken = default)
    {
        var response = await client.LoginOrRegisterWithGoogleAsync(
            new LoginWithGoogleRequest { GoogleSub = googleSub, Email = email },
            cancellationToken: cancellationToken);
        if (!response.Succeeded)
            return null;
        return new LoginResponse(response.SessionToken, response.UserId, response.Role, email);
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

    public async Task<IReadOnlyList<ApiKeyInfo>> ListApiKeysAsync(string userId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var response = await client.ListApiKeysAsync(new ListApiKeysRequest { UserId = userId, IncludeDeleted = includeDeleted }, cancellationToken: cancellationToken);
        return response.Keys.Select(k => new ApiKeyInfo(
            k.Id,
            k.Name,
            k.Prefix,
            DateTimeOffset.FromUnixTimeSeconds(k.CreatedAtUnixTimeSeconds),
            k.LastUsedAtUnixTimeSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(k.LastUsedAtUnixTimeSeconds) : null,
            k.IsActive,
            k.IsDeleted)).ToList();
    }

    public async Task RenameApiKeyAsync(string userId, string apiKeyId, string name, CancellationToken cancellationToken = default)
    {
        var response = await client.RenameApiKeyAsync(new RenameApiKeyRequest { UserId = userId, ApiKeyId = apiKeyId, Name = name }, cancellationToken: cancellationToken);
        if (!response.IsRenamed)
        {
            throw new AuthException(response.ErrorCode);
        }
    }

    public async Task RevokeApiKeyAsync(string userId, string apiKeyId, CancellationToken cancellationToken = default)
    {
        var response = await client.RevokeApiKeyAsync(new RevokeApiKeyRequest { UserId = userId, ApiKeyId = apiKeyId }, cancellationToken: cancellationToken);
        if (!response.IsRevoked)
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
            u.MaxApiKeys)).ToList();
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
            u.MaxApiKeys);
    }

    public async Task UpdateUserAsync(string userId, string? displayName, int maxApiKeys, CancellationToken cancellationToken = default)
    {
        var request = new UpdateUserRequest
        {
            UserId = userId,
            MaxApiKeys = maxApiKeys
        };
        if (displayName != null) request.DisplayName = displayName;

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
