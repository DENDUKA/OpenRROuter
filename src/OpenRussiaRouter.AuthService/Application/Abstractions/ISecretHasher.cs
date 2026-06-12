namespace OpenRussiaRouter.AuthService.Application.Abstractions;

public interface ISecretHasher
{
    string Hash(string secret);
    bool Verify(string rawSecret, string hash);
}
