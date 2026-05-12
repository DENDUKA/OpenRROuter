using OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;

namespace OpenRussiaRouter.WebApp.Services.Auth;

public sealed record LoginResponse(string SessionToken, string UserId, string Role);

public sealed record InviteCompletionResponse(string UserId, string Email, string Role);

public sealed record ApiKeyInfo(string Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsed, bool IsActive);

public sealed record UserInfo(string Id, string Email, string Role, string Status, DateTimeOffset CreatedAt);

public sealed record CreateApiKeyResponse(string ApiKeyId, string RawKey, string Name);

public sealed class AuthException(string ErrorCode) : Exception
{
    public string ErrorCode { get; } = ErrorCode;
}

public sealed class AuthClient(AuthGrpc.AuthServiceClient client)
{
    public async Task<LoginResponse> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var response = await client.LoginAsync(new LoginRequest { Email = email, Password = password }, cancellationToken: cancellationToken);
        if (!response.Succeeded)
        {
            throw new AuthException(response.ErrorCode);
        }
        return new LoginResponse(response.SessionToken, response.UserId, response.Role);
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

    public async Task<IReadOnlyList<ApiKeyInfo>> ListApiKeysAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await client.ListApiKeysAsync(new ListApiKeysRequest { UserId = userId }, cancellationToken: cancellationToken);
        return response.Keys.Select(k => new ApiKeyInfo(
            k.Id,
            k.Name,
            k.Prefix,
            DateTimeOffset.FromUnixTimeSeconds(k.CreatedAtUnixTimeSeconds),
            k.LastUsedAtUnixTimeSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(k.LastUsedAtUnixTimeSeconds) : null,
            k.IsActive)).ToList();
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

    public async Task<IReadOnlyList<UserInfo>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.ListUsersAsync(new ListUsersRequest(), cancellationToken: cancellationToken);
        return response.Users.Select(u => new UserInfo(
            u.Id,
            u.Email,
            u.Role,
            u.Status,
            DateTimeOffset.FromUnixTimeSeconds(u.CreatedAtUnixTimeSeconds))).ToList();
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