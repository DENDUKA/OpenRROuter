using Grpc.Core;
using Grpc.Core.Interceptors;

namespace OpenRussiaRouter.BuildingBlocks.Grpc;

/// <summary>
/// Client-side gRPC interceptor that applies a default deadline to outgoing calls that do not
/// already specify one. Without a deadline a gRPC call can hang indefinitely when the remote
/// service is slow or unresponsive; this guarantees an upper bound on every call.
/// </summary>
/// <param name="defaultDeadline">The maximum duration allowed for a call that has no explicit deadline.</param>
public sealed class GrpcDeadlineInterceptor(TimeSpan defaultDeadline) : Interceptor
{
    /// <inheritdoc />
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, ApplyDeadline(context));

    /// <inheritdoc />
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, ApplyDeadline(context));

    /// <inheritdoc />
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, ApplyDeadline(context));

    private ClientInterceptorContext<TRequest, TResponse> ApplyDeadline<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        if (context.Options.Deadline.HasValue)
            return context;

        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithDeadline(DateTime.UtcNow.Add(defaultDeadline)));
    }
}
