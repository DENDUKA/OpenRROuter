namespace OpenRussiaRouter.ApiGateway.Router;

/// <summary>
/// Response returned by a 9Router node.
/// </summary>
/// <param name="StatusCode">HTTP status code.</param>
/// <param name="ContentType">Response content type.</param>
/// <param name="Body">Response body.</param>
public sealed record RouterProxyResponse(int StatusCode, string ContentType, string Body);

