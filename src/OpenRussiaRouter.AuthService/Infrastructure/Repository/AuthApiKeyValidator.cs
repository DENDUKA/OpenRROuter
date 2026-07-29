using OpenRussiaRouter.AuthService.Application.Abstractions;
using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.Auth;

namespace OpenRussiaRouter.AuthService.Infrastructure.Repository;

public sealed class DapperAuthApiKeyValidator(
    IAuthRepository repository,
    ISecretHasher secretHasher)
    : IAuthApiKeyValidator
{
    public async Task<ValidateApiKeyResult> ValidateAsync(string rawKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return Invalid();

        var apiKey = await repository.FindActiveApiKeyBySecretHashAsync(secretHasher.Hash(rawKey), ct).ConfigureAwait(false);
        if (apiKey is null)
            return Invalid();

        await repository.MarkApiKeyUsedAsync(apiKey.Id, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        return new ValidateApiKeyResult(true, apiKey.UserId, apiKey.Id, apiKey.Prefix, null, apiKey.UserRole?.ToString());
    }

    private static ValidateApiKeyResult Invalid() =>
        new(false, null, null, null, ErrorCodes.InvalidApiKey, null);
}
