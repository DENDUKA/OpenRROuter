using System.Text;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Router;

public sealed class RouterStreamSession : IAsyncDisposable
{
    private readonly HttpResponseMessage _response;
    private bool _disposed;

    internal RouterStreamSession(HttpResponseMessage response)
    {
        _response = response;
    }

    public int StatusCode => (int)_response.StatusCode;
    public bool IsSuccess => _response.IsSuccessStatusCode;

    public string? ProviderName => _response.Headers.TryGetValues("x-9r-provider", out var pv) ? pv.FirstOrDefault() : null;
    public string? ProviderAccount => _response.Headers.TryGetValues("x-9r-account", out var av) ? av.FirstOrDefault() : null;

    public async Task<string> ReadErrorBodyAsync(CancellationToken cancellationToken)
        => await _response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Reads SSE chunks from 9Router and forwards them to <paramref name="destination"/>.
    /// Returns the <see cref="TokenUsage"/> extracted from the final usage chunk, or default if absent.
    /// </summary>
    public async Task<TokenUsage> CopyToAsync(Stream destination, CancellationToken cancellationToken)
    {
        var stream = await _response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        TokenUsage usage = default;

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line.Substring(6);

            if (data == "[DONE]")
            {
                await destination.WriteAsync("data: [DONE]\n\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                break;
            }

            if (RouterUsageExtractor.TryExtract(data, out var chunkUsage) &&
                (chunkUsage.InputTokens > 0 || chunkUsage.OutputTokens > 0))
            {
                usage = chunkUsage;
            }

            var bytes = Encoding.UTF8.GetBytes("data: " + data + "\n\n");
            await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        return usage;
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _response.Dispose();
            _disposed = true;
        }
        return ValueTask.CompletedTask;
    }
}
