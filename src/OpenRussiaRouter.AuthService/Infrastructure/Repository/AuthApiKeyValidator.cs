using OpenRussiaRouter.AuthService.Application.Services;
using OpenRussiaRouter.AuthService.Infrastructure.Security;
using OpenRussiaRouter.BuildingBlocks.Errors;
using OpenRussiaRouter.Contracts.Auth;

namespace OpenRussiaRouter.AuthService.Infrastructure.Repository;

public sealed class DapperAuthApiKeyValidator(
    IAuthRepository repository,
    Sha256SecretHasher secretHasher)
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
        var user = await repository.GetUserAsync(apiKey.UserId, ct).ConfigureAwait(false);
        return new ValidateApiKeyResult(true, apiKey.UserId, apiKey.Id, apiKey.Prefix, null, user?.Role.ToString());
    }

    private static ValidateApiKeyResult Invalid() =>
        new(false, null, null, null, ErrorCodes.InvalidApiKey, null);
}
