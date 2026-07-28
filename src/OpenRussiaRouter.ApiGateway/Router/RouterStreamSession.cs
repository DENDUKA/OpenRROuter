using System.Buffers;
using System.Text;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Router;

public sealed class RouterStreamSession : IAsyncDisposable
{
    private readonly HttpResponseMessage _response;
    private bool _disposed;

    private static readonly Encoding Utf8 = Encoding.UTF8;

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
        using var reader = new StreamReader(stream, Utf8, leaveOpen: true);
        TokenUsage usage = default;

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;

            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var dataStart = "data:".Length;
            while (dataStart < line.Length && line[dataStart] == ' ')
                dataStart++;

            var data = line.Substring(dataStart);

            if (data == "[DONE]")
            {
                await WriteSseLineAsync(destination, "[DONE]", cancellationToken).ConfigureAwait(false);
                break;
            }

            // Fast path: skip expensive JSON parse when the chunk carries no usage.
            // Only the final chunk (with stream_options.include_usage) contains "usage".
            if (data.Contains("\"usage\"", StringComparison.Ordinal) &&
                TokenUsageExtractor.TryExtractJson(data, out var chunkUsage) &&
                (chunkUsage.InputTokens > 0 || chunkUsage.OutputTokens > 0))
            {
                usage = chunkUsage;
            }

            await WriteSseLineAsync(destination, data, cancellationToken).ConfigureAwait(false);
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        return usage;
    }

    private static async Task WriteSseLineAsync(Stream destination, string data, CancellationToken cancellationToken)
    {
        var prefixByteCount = Utf8.GetByteCount("data: ");
        var dataByteCount = Utf8.GetByteCount(data);
        var suffixByteCount = Utf8.GetByteCount("\n\n");
        var totalLength = prefixByteCount + dataByteCount + suffixByteCount;
        var buffer = ArrayPool<byte>.Shared.Rent(totalLength);
        try
        {
            Utf8.GetBytes("data: ", 0, 6, buffer, 0);
            Utf8.GetBytes(data, 0, data.Length, buffer, prefixByteCount);
            Utf8.GetBytes("\n\n", 0, 2, buffer, prefixByteCount + dataByteCount);
            await destination.WriteAsync(buffer, 0, totalLength, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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
