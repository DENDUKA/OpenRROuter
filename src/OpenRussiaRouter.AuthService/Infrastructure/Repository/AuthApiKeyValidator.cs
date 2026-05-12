using Dapper;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Infrastructure.Persistence;
using OpenRussiaRouter.AuthService.Infrastructure.Security;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.Auth;

namespace OpenRussiaRouter.AuthService.Infrastructure.Repository;

/// <summary>
/// Dapper/PostgreSQL-backed API key validator.
/// </summary>
public sealed class DapperAuthApiKeyValidator(
    IAuthDbConnectionFactory connectionFactory,
    Sha256SecretHasher secretHasher)
    : IAuthApiKeyValidator
{
    private const string SelectApiKeySql = """
        select
            ak.id as ApiKeyId,
            ak.user_id as UserId
        from auth.api_keys ak
        inner join auth.users u on u.id = ak.user_id
        where ak.secret_hash = @SecretHash
          and ak.revoked_at_utc is null
          and u.status = 'Active'
        limit 1;
        """;

    private const string MarkUsedSql = """
        update auth.api_keys
        set last_used_at_utc = @LastUsedAtUtc
        where id = @ApiKeyId;
        """;

    /// <inheritdoc />
    public ValidateApiKeyResult Validate(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return Invalid();
        }

        using var connection = connectionFactory.CreateConnection();
        var row = connection.QuerySingleOrDefault<ApiKeyValidationRow>(
            SelectApiKeySql,
            new { SecretHash = secretHasher.Hash(rawKey) });
        if (row is null)
        {
            return Invalid();
        }

        connection.Execute(MarkUsedSql, new { LastUsedAtUtc = DateTimeOffset.UtcNow, row.ApiKeyId });
        return new ValidateApiKeyResult(true, row.UserId, row.ApiKeyId, null);
    }

    private static ValidateApiKeyResult Invalid()
    {
        return new ValidateApiKeyResult(false, null, null, ErrorCodes.InvalidApiKey);
    }

    private sealed record ApiKeyValidationRow(string ApiKeyId, string UserId);
}
