using OpenRussiaRouter.Contracts.Auth;

namespace OpenRussiaRouter.AuthService.Application.Services;

public interface IAuthApiKeyValidator
{
    Task<ValidateApiKeyResult> ValidateAsync(string rawKey, CancellationToken ct = default);
}

