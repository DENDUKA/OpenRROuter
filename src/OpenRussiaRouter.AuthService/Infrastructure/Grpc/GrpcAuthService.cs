using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;

namespace OpenRussiaRouter.AuthService.Infrastructure.Grpc;

public sealed class GrpcAuthService(
    IAuthApiKeyValidator apiKeyValidator,
    IAuthRepository repository,
    AuthAccountApplicationService accounts,
    LoginService loginService,
    GoogleAuthService googleAuthService,
    ILogger<GrpcAuthService> logger) : AuthGrpc.AuthServiceBase
{
    public override async Task<ValidateApiKeyResponse> ValidateApiKey(
        ValidateApiKeyRequest request,
        ServerCallContext context)
    {
        var result = await apiKeyValidator.ValidateAsync(request.RawKey, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

        return new ValidateApiKeyResponse
        {
            IsValid = result.IsValid,
            UserId = result.UserId ?? string.Empty,
            ApiKeyId = result.ApiKeyId ?? string.Empty,
            ApiKeyPrefix = result.ApiKeyPrefix ?? string.Empty,
            ErrorCode = result.ErrorCode ?? string.Empty,
            Role = result.Role ?? string.Empty
        };
    }

    public override async Task<LoginResponse> Login(
        LoginRequest request,
        ServerCallContext context)
    {
        var user = await repository.GetUserByEmailAsync(request.Email, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        var result = loginService.Login(user, request.Password, DateTimeOffset.UtcNow, TimeSpan.FromDays(7));

        if (!result.Succeeded)
            return new LoginResponse { Succeeded = false, ErrorCode = result.ErrorCode ?? ErrorCodes.AuthenticationRequired };

        return new LoginResponse
        {
            Succeeded = true,
            SessionToken = result.Session!.Token,
            UserId = result.Session.UserId,
            Role = result.Session.Role.ToString()
        };
    }

    public override async Task<LoginResponse> LoginOrRegisterWithGoogle(
        LoginWithGoogleRequest request,
        ServerCallContext context)
    {
        var result = await googleAuthService.LoginOrRegisterWithGoogleAsync(
            request.GoogleSub,
            request.Email,
            context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

        if (!result.Succeeded)
            return new LoginResponse { Succeeded = false, ErrorCode = result.ErrorCode ?? ErrorCodes.AuthenticationRequired };

        return new LoginResponse
        {
            Succeeded = true,
            SessionToken = result.Session!.Token,
            UserId = result.Session.UserId,
            Role = result.Session.Role.ToString()
        };
    }

    public override async Task<CreateInviteResponse> CreateInvite(
        CreateInviteRequest request,
        ServerCallContext context)
    {
        try
        {
            var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(request.ExpiresAtUnixTimeSeconds);
            var created = await accounts.CreateInviteAsync(
                request.Email,
                ParseRole(request.Role),
                request.CreatedByAdminId,
                expiresAtUtc,
                DateTimeOffset.UtcNow,
                context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            return new CreateInviteResponse
            {
                IsCreated = true,
                InviteId = created.Invite.Id,
                RawToken = created.RawToken,
                Email = created.Invite.Email,
                Role = created.Invite.Role.ToString(),
                ExpiresAtUnixTimeSeconds = created.Invite.ExpiresAtUtc.ToUnixTimeSeconds()
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            logger.LogWarning(ex, "CreateInvite failed. Email: {Email}; Role: {Role}", request.Email, request.Role);
            return new CreateInviteResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<CompleteInviteResponse> CompleteInvite(
        CompleteInviteRequest request,
        ServerCallContext context)
    {
        try
        {
            var user = await accounts.CompleteInviteAsync(
                request.RawToken,
                request.Password,
                DateTimeOffset.UtcNow,
                context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            return new CompleteInviteResponse
            {
                IsCompleted = true,
                UserId = user.Id,
                Email = user.Email,
                Role = user.Role.ToString()
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            logger.LogWarning(ex, "CompleteInvite failed.");
            return new CompleteInviteResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<CreateApiKeyResponse> CreateApiKey(
        CreateApiKeyRequest request,
        ServerCallContext context)
    {
        try
        {
            var created = await accounts.CreateApiKeyAsync(
                request.UserId,
                request.Name,
                DateTimeOffset.UtcNow,
                context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            return new CreateApiKeyResponse
            {
                IsCreated = true,
                ApiKeyId = created.ApiKey.Id,
                RawKey = created.RawKey,
                Prefix = created.ApiKey.Prefix,
                Name = created.ApiKey.Name,
                CreatedAtUnixTimeSeconds = created.ApiKey.CreatedAtUtc.ToUnixTimeSeconds()
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            logger.LogWarning(ex, "CreateApiKey failed. UserId: {UserId}; Name: {Name}", request.UserId, request.Name);
            return new CreateApiKeyResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<RevokeApiKeyResponse> RevokeApiKey(
        RevokeApiKeyRequest request,
        ServerCallContext context)
    {
        try
        {
            await accounts.RevokeApiKeyAsync(request.UserId, request.ApiKeyId, DateTimeOffset.UtcNow, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            return new RevokeApiKeyResponse { IsRevoked = true };
        }
        catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException)
        {
            logger.LogWarning(ex, "RevokeApiKey failed. UserId: {UserId}; ApiKeyId: {ApiKeyId}", request.UserId, request.ApiKeyId);
            return new RevokeApiKeyResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<ListApiKeysResponse> ListApiKeys(
        ListApiKeysRequest request,
        ServerCallContext context)
    {
        var keys = await accounts.ListApiKeysAsync(request.UserId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        return new ListApiKeysResponse
        {
            Keys =
            {
                keys.Select(k => new ApiKeyInfo
                {
                    Id = k.Id,
                    Name = k.Name,
                    Prefix = k.Prefix,
                    CreatedAtUnixTimeSeconds = k.CreatedAtUtc.ToUnixTimeSeconds(),
                    LastUsedAtUnixTimeSeconds = k.LastUsedAtUtc?.ToUnixTimeSeconds() ?? 0,
                    RevokedAtUnixTimeSeconds = k.RevokedAtUtc?.ToUnixTimeSeconds() ?? 0,
                    IsActive = k.IsActive
                })
            }
        };
    }

    public override async Task<RenameApiKeyResponse> RenameApiKey(
        RenameApiKeyRequest request,
        ServerCallContext context)
    {
        try
        {
            await accounts.RenameApiKeyAsync(request.UserId, request.ApiKeyId, request.Name, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            return new RenameApiKeyResponse { IsRenamed = true };
        }
        catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException)
        {
            logger.LogWarning(ex, "RenameApiKey failed. UserId: {UserId}; ApiKeyId: {ApiKeyId}; Name: {Name}", request.UserId, request.ApiKeyId, request.Name);
            return new RenameApiKeyResponse { ErrorCode = ErrorCodes.ValidationFailed };
        }
    }

    public override async Task<ListUsersResponse> ListUsers(
        ListUsersRequest request,
        ServerCallContext context)
    {
        var users = await accounts.ListUsersAsync(context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        return new ListUsersResponse
        {
            Users =
            {
                users.Select(u => new UserInfo
                {
                    Id = u.Id,
                    Email = u.Email,
                    Role = u.Role.ToString(),
                    Status = u.Status.ToString(),
                    CreatedAtUnixTimeSeconds = u.CreatedAtUtc.ToUnixTimeSeconds()
                })
            }
        };
    }

    public override async Task<ListActiveApiKeyHashesResponse> ListActiveApiKeyHashes(
        ListActiveApiKeyHashesRequest request,
        ServerCallContext context)
    {
        var hashes = await repository.ListActiveApiKeyHashesAsync(context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        var response = new ListActiveApiKeyHashesResponse();
        response.Keys.AddRange(hashes.Select(h => new ApiKeyHashEntry
        {
            Prefix = h.Prefix,
            SecretHash = h.SecretHash
        }));
        return response;
    }

    public override async Task<GetFirstActiveApiKeyRawResponse> GetFirstActiveApiKeyRaw(
        GetFirstActiveApiKeyRawRequest request,
        ServerCallContext context)
    {
        var rawKey = await repository.GetFirstActiveApiKeyRawAsync(request.UserId, context?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        return new GetFirstActiveApiKeyRawResponse { RawKey = rawKey ?? string.Empty };
    }

    private static UserRole ParseRole(string role)
    {
        return Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException("Unknown role.", nameof(role));
    }
}
