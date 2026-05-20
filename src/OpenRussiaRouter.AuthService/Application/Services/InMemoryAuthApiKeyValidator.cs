using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

using OpenRussiaRouter.AuthService.Infrastructure.Security;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.Auth;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class InMemoryAuthApiKeyValidator : IAuthApiKeyValidator
{
    private const int VisiblePrefixLength = 12;

    private readonly ConcurrentDictionary<string, StoredApiKey> _keysByHash = new(StringComparer.Ordinal);
    private readonly IAuthRepository? repository;
    private readonly Sha256SecretHasher _secretHasher;

    public InMemoryAuthApiKeyValidator(Sha256SecretHasher secretHasher, IConfiguration configuration, IAuthRepository? repository = null)
    {
        this.repository = repository;
        _secretHasher = secretHasher;
        LoadSeedKeys(configuration);
    }

    public ValidateApiKeyResult Validate(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return Invalid();
        }

        var hash = _secretHasher.Hash(rawKey);
        if (repository?.FindActiveApiKeyBySecretHash(hash) is { } apiKey)
        {
            return new ValidateApiKeyResult(true, apiKey.UserId, apiKey.Id, apiKey.Prefix, null);
        }

        if (!_keysByHash.TryGetValue(hash, out var key) || key.Revoked)
        {
            return Invalid();
        }

        return new ValidateApiKeyResult(true, key.UserId, key.ApiKeyId, key.ApiKeyPrefix, null);
    }

    public void RegisterRawKey(string rawKey, string userId, string apiKeyId)
    {
        var key = new StoredApiKey(userId, apiKeyId, GetPrefix(rawKey), Revoked: false);
        _keysByHash[_secretHasher.Hash(rawKey)] = key;
    }

    private static ValidateApiKeyResult Invalid()
    {
        return new ValidateApiKeyResult(false, null, null, null, ErrorCodes.InvalidApiKey);
    }

    private static string GetPrefix(string rawKey)
    {
        return rawKey[..Math.Min(rawKey.Length, VisiblePrefixLength)];
    }

    private void LoadSeedKeys(IConfiguration configuration)
    {
        foreach (var section in configuration.GetSection("Auth:SeedApiKeys").GetChildren())
        {
            var rawKey = section["RawKey"];
            var userId = section["UserId"];
            var apiKeyId = section["ApiKeyId"];

            if (string.IsNullOrWhiteSpace(rawKey) ||
                string.IsNullOrWhiteSpace(userId) ||
                string.IsNullOrWhiteSpace(apiKeyId))
            {
                continue;
            }

            RegisterRawKey(rawKey, userId, apiKeyId);
        }
    }

    private sealed record StoredApiKey(string UserId, string ApiKeyId, string ApiKeyPrefix, bool Revoked);
}
