using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.AuthService.Application.Interfaces;
using OpenRussiaRouter.AuthService.Infrastructure.Security;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.Auth;

namespace OpenRussiaRouter.AuthService.Application.Services;

/// <summary>
/// In-memory API key validator used until PostgreSQL persistence is added.
/// </summary>
public sealed class InMemoryAuthApiKeyValidator : IAuthApiKeyValidator
{
    private readonly ConcurrentDictionary<string, StoredApiKey> _keysByHash = new(StringComparer.Ordinal);
    private readonly IAuthRepository? repository;
    private readonly Sha256SecretHasher _secretHasher;

    /// <summary>
    /// Creates an in-memory validator.
    /// </summary>
    /// <param name="secretHasher">Secret hasher.</param>
    /// <param name="configuration">Application configuration with optional seed keys.</param>
    public InMemoryAuthApiKeyValidator(Sha256SecretHasher secretHasher, IConfiguration configuration, IAuthRepository? repository = null)
    {
        this.repository = repository;
        _secretHasher = secretHasher;
        LoadSeedKeys(configuration);
    }

    /// <inheritdoc />
    public ValidateApiKeyResult Validate(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return Invalid();
        }

        var hash = _secretHasher.Hash(rawKey);
        if (repository is InMemoryAuthRepository inMemoryRepository &&
            inMemoryRepository.GetApiKeyBySecretHash(hash) is { IsActive: true } apiKey)
        {
            return new ValidateApiKeyResult(true, apiKey.UserId, apiKey.Id, null);
        }

        if (!_keysByHash.TryGetValue(hash, out var key) || key.Revoked)
        {
            return Invalid();
        }

        return new ValidateApiKeyResult(true, key.UserId, key.ApiKeyId, null);
    }

    /// <summary>
    /// Registers a raw key for tests or bootstrap flows.
    /// </summary>
    /// <param name="rawKey">Raw API key.</param>
    /// <param name="userId">Owner user id.</param>
    /// <param name="apiKeyId">API key id.</param>
    public void RegisterRawKey(string rawKey, string userId, string apiKeyId)
    {
        var key = new StoredApiKey(userId, apiKeyId, Revoked: false);
        _keysByHash[_secretHasher.Hash(rawKey)] = key;
    }

    private static ValidateApiKeyResult Invalid()
    {
        return new ValidateApiKeyResult(false, null, null, ErrorCodes.InvalidApiKey);
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

    private sealed record StoredApiKey(string UserId, string ApiKeyId, bool Revoked);
}
