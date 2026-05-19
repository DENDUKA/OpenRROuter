namespace OpenRussiaRouter.AuthService.Infrastructure.Security;

public interface ISecretGenerator
{

    string Generate(string prefix, int bytes = 32, char separator = '_');
}
