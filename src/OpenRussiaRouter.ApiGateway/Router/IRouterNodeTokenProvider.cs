namespace OpenRussiaRouter.ApiGateway.Router;

public interface IRouterNodeTokenProvider
{

    string Resolve(string tokenSecretName);
}

