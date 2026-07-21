using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.ContentService.Infrastructure.Auth;

/// <summary>
/// Server-side gRPC interceptor enforcing s2s authentication on the admin ContentService methods.
/// See <see cref="ServiceAuthOptions"/> for the configuration model.
/// </summary>
public sealed class ServiceAuthInterceptor(
    IOptions<ServiceAuthOptions> options,
    ILogger<ServiceAuthInterceptor> logger) : Interceptor
{
    /// <summary>
    /// The admin surface. Owned in code so that a missing config policy fails closed rather than
    /// silently opening a method. ListToolImages is here too: it leaks draft asset names.
    /// </summary>
    public static readonly IReadOnlySet<string> ProtectedMethods = new HashSet<string>(StringComparer.Ordinal)
    {
        "ListToolsForAdmin",
        "GetToolForAdmin",
        "UpsertTool",
        "DeleteTool",
        "SetToolPublished",
        "ListToolImages",
        "UploadToolImage",
        "DeleteToolImage",
        "UpsertToolCategory",
        "DeleteToolCategory"
    };

    private readonly ServiceAuthOptions auth = options.Value;

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        EnforceAuth(context);
        return await continuation(request, context).ConfigureAwait(false);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnforceAuth(context);
        return await continuation(requestStream, context).ConfigureAwait(false);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnforceAuth(context);
        await continuation(request, responseStream, context).ConfigureAwait(false);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnforceAuth(context);
        await continuation(requestStream, responseStream, context).ConfigureAwait(false);
    }

    private void EnforceAuth(ServerCallContext context)
    {
        var methodName = NormalizeMethodName(context.Method);

        if (!ProtectedMethods.Contains(methodName))
        {
            // Public catalog reads stay open.
            return;
        }

        if (!auth.MethodPolicies.TryGetValue(methodName, out var allowedCallers) || allowedCallers.Length == 0)
        {
            logger.LogError(
                "S2S auth rejected: method={Method} peer={Peer} reason=missing_method_policy",
                methodName, context.Peer);
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                $"S2S authorization policy is not configured for {methodName}."));
        }

        var callerEntries = context.RequestHeaders.Get(ServiceAuthOptions.CallerMetadataKey);
        var tokenEntries = context.RequestHeaders.Get(ServiceAuthOptions.TokenMetadataKey);

        if (callerEntries is null || tokenEntries is null)
        {
            logger.LogWarning(
                "S2S auth rejected: method={Method} peer={Peer} reason=missing_metadata",
                methodName, context.Peer);
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                $"S2S token required for {methodName}."));
        }

        var declaredCaller = callerEntries.Value;
        var providedToken = tokenEntries.Value;

        if (string.IsNullOrEmpty(declaredCaller) || string.IsNullOrEmpty(providedToken))
        {
            logger.LogWarning(
                "S2S auth rejected: method={Method} peer={Peer} reason=empty_metadata",
                methodName, context.Peer);
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                $"S2S token required for {methodName}."));
        }

        if (!auth.Tokens.TryGetValue(declaredCaller, out var expectedToken) || expectedToken.Length == 0)
        {
            logger.LogWarning(
                "S2S auth rejected: method={Method} peer={Peer} caller={Caller} reason=unknown_caller",
                methodName, context.Peer, declaredCaller);
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                $"Unknown caller '{declaredCaller}' for {methodName}."));
        }

        if (!ConstantTimeEquals(expectedToken, providedToken))
        {
            logger.LogWarning(
                "S2S auth rejected: method={Method} peer={Peer} caller={Caller} reason=token_mismatch",
                methodName, context.Peer, declaredCaller);
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                $"Invalid S2S token for {methodName}."));
        }

        if (Array.IndexOf(allowedCallers, declaredCaller) < 0)
        {
            logger.LogWarning(
                "S2S auth rejected: method={Method} peer={Peer} caller={Caller} reason=not_in_method_allowlist",
                methodName, context.Peer, declaredCaller);
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                $"Caller '{declaredCaller}' is not permitted to invoke {methodName}."));
        }

        var httpContext = context.GetHttpContext();
        if (httpContext is not null)
        {
            httpContext.Items["__orr_s2s_caller"] = declaredCaller;
        }
    }

    private static string NormalizeMethodName(string fullMethod)
    {
        // fullMethod looks like "/openrussiarouter.content.v1.ContentService/UpsertTool".
        var slash = fullMethod.LastIndexOf('/');
        return slash >= 0 && slash < fullMethod.Length - 1
            ? fullMethod[(slash + 1)..]
            : fullMethod;
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
