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
    public ValidateApiKeyResult Validate(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return Invalid();
        }

        var apiKey = repository.FindActiveApiKeyBySecretHash(secretHasher.Hash(rawKey));
        if (apiKey is null)
        {
            return Invalid();
        }

        repository.MarkApiKeyUsed(apiKey.Id, DateTimeOffset.UtcNow);
        return new ValidateApiKeyResult(true, apiKey.UserId, apiKey.Id, apiKey.Prefix, null);
    }

    private static ValidateApiKeyResult Invalid()
    {
        return new ValidateApiKeyResult(false, null, null, null, ErrorCodes.InvalidApiKey);
    }
}
