using Grpc.Core;
using Grpc.Core.Interceptors;

namespace OpenRussiaRouter.BuildingBlocks.Grpc;

/// <summary>
/// Client-side gRPC interceptor that attaches s2s (service-to-service) authentication
/// metadata to outgoing calls. Configure via constructor:
/// <list type="bullet">
///   <item><c>callerId</c> — logical caller identity (e.g. <c>apigateway</c>, <c>webappadmin</c>).</item>
///   <item><c>token</c> — shared secret for that caller, configured server-side.</item>
/// </list>
/// Pairs with <c>BillingService.Infrastructure.Auth.ServiceAuthInterceptor</c> on the server.
/// </summary>
public sealed class ServiceAuthClientInterceptor(string callerId, string token) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(request, AttachAuth(context));
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(request, AttachAuth(context));
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(AttachAuth(context));
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(AttachAuth(context));
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(request, AttachAuth(context));
    }

    private ClientInterceptorContext<TRequest, TResponse> AttachAuth<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        // Build a fresh Metadata so we don't mutate the caller's headers. Copy any existing
        // entries verbatim and then add the s2s credentials on top.
        var headers = new Metadata();
        if (context.Options.Headers is not null)
        {
            foreach (var entry in context.Options.Headers)
            {
                headers.Add(entry);
            }
        }

        // BillingService's interceptor reads exactly these two keys (lowercase, ASCII per gRPC convention).
        headers.Add("x-orr-caller", callerId);
        headers.Add("x-orr-s2s-token", token);

        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(headers));
    }
}
