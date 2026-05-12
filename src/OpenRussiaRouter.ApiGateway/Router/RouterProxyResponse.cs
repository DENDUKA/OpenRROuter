namespace OpenRussiaRouter.ApiGateway.Router;

public sealed record RouterProxyResponse(int StatusCode, string ContentType, string Body);

