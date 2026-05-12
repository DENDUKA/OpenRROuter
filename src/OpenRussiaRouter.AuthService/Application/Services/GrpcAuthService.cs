using Grpc.Core;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class GrpcAuthService(
    IAuthApiKeyValidator apiKeyValidator,
    IAuthRepository repository,
    AuthAccountApplicationService accounts,
    LoginService loginService) : AuthGrpc.AuthServiceBase
{

    public override Task<ValidateApiKeyResponse> ValidateApiKey(
        ValidateApiKeyRequest request,
        ServerCallContext context)
    {
        var result = apiKeyValidator.Validate(request.RawKey);

        return Task.FromResult(new ValidateApiKeyResponse
        {
            IsValid = result.IsValid,
            UserId = result.UserId ?? string.Empty,
            ApiKeyId = result.ApiKeyId ?? string.Empty,
            ErrorCode = result.ErrorCode ?? string.Empty
        });
    }

    public override Task<LoginResponse> Login(
        LoginRequest request,
        ServerCallContext context)
    {
        var user = repository.GetUserByEmail(request.Email);
        var result = loginService.Login(user, request.Password, DateTimeOffset.UtcNow, TimeSpan.FromDays(7));

        if (!result.Succeeded)
        {
            return Task.FromResult(new LoginResponse { Succeeded = false, ErrorCode = result.ErrorCode ?? ErrorCodes.AuthenticationRequired });
        }

        return Task.FromResult(new LoginResponse
        {
            Succeeded = true,
            SessionToken = result.Session!.Token,
            UserId = result.Session.UserId,
            Role = result.Session.Role.ToString()
        });
    }

    public override Task<CreateInviteResponse> CreateInvite(
        CreateInviteRequest request,
        ServerCallContext context)
    {
        try
        {
            var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(request.ExpiresAtUnixTimeSeconds);
            var created = accounts.CreateInvite(
                request.Email,
                ParseRole(request.Role),
                request.CreatedByAdminId,
                expiresAtUtc,
                DateTimeOffset.UtcNow);

            return Task.FromResult(new CreateInviteResponse
            {
                IsCreated = true,
                InviteId = created.Invite.Id,
                RawToken = created.RawToken,
                Email = created.Invite.Email,
                Role = created.Invite.Role.ToString(),
                ExpiresAtUnixTimeSeconds = created.Invite.ExpiresAtUtc.ToUnixTimeSeconds()
            });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Task.FromResult(new CreateInviteResponse { ErrorCode = ErrorCodes.ValidationFailed });
        }
    }

    public override Task<CompleteInviteResponse> CompleteInvite(
        CompleteInviteRequest request,
        ServerCallContext context)
    {
        try
        {
            var user = accounts.CompleteInvite(request.RawToken, request.Password, DateTimeOffset.UtcNow);
            return Task.FromResult(new CompleteInviteResponse
            {
                IsCompleted = true,
                UserId = user.Id,
                Email = user.Email,
                Role = user.Role.ToString()
            });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Task.FromResult(new CompleteInviteResponse { ErrorCode = ErrorCodes.ValidationFailed });
        }
    }

    public override Task<CreateApiKeyResponse> CreateApiKey(
        CreateApiKeyRequest request,
        ServerCallContext context)
    {
        try
        {
            var created = accounts.CreateApiKey(request.UserId, request.Name, DateTimeOffset.UtcNow);
            return Task.FromResult(new CreateApiKeyResponse
            {
                IsCreated = true,
                ApiKeyId = created.ApiKey.Id,
                RawKey = created.RawKey,
                Prefix = created.ApiKey.Prefix,
                Name = created.ApiKey.Name,
                CreatedAtUnixTimeSeconds = created.ApiKey.CreatedAtUtc.ToUnixTimeSeconds()
            });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Task.FromResult(new CreateApiKeyResponse { ErrorCode = ErrorCodes.ValidationFailed });
        }
    }

    public override Task<RevokeApiKeyResponse> RevokeApiKey(
        RevokeApiKeyRequest request,
        ServerCallContext context)
    {
        try
        {
            accounts.RevokeApiKey(request.UserId, request.ApiKeyId, DateTimeOffset.UtcNow);
            return Task.FromResult(new RevokeApiKeyResponse { IsRevoked = true });
        }
        catch (KeyNotFoundException)
        {
            return Task.FromResult(new RevokeApiKeyResponse { ErrorCode = ErrorCodes.ValidationFailed });
        }
        catch (ArgumentException)
        {
            return Task.FromResult(new RevokeApiKeyResponse { ErrorCode = ErrorCodes.ValidationFailed });
        }
    }

    public override Task<ListApiKeysResponse> ListApiKeys(
        ListApiKeysRequest request,
        ServerCallContext context)
    {
        var keys = accounts.ListApiKeys(request.UserId);
        return Task.FromResult(new ListApiKeysResponse
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
        });
    }

    public override Task<RenameApiKeyResponse> RenameApiKey(
        RenameApiKeyRequest request,
        ServerCallContext context)
    {
        try
        {
            accounts.RenameApiKey(request.UserId, request.ApiKeyId, request.Name);
            return Task.FromResult(new RenameApiKeyResponse { IsRenamed = true });
        }
        catch (KeyNotFoundException)
        {
            return Task.FromResult(new RenameApiKeyResponse { ErrorCode = ErrorCodes.ValidationFailed });
        }
        catch (ArgumentException)
        {
            return Task.FromResult(new RenameApiKeyResponse { ErrorCode = ErrorCodes.ValidationFailed });
        }
    }

    public override Task<ListUsersResponse> ListUsers(
        ListUsersRequest request,
        ServerCallContext context)
    {
        var users = accounts.ListUsers();
        return Task.FromResult(new ListUsersResponse
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
        });
    }

    private static UserRole ParseRole(string role)
    {
        return Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException("Unknown role.", nameof(role));
    }
}

