using OpenRussiaRouter.Contracts.Auth;

namespace OpenRussiaRouter.AuthService.Application.Services;

public interface IAuthApiKeyValidator
{

    ValidateApiKeyResult Validate(string rawKey);
}

