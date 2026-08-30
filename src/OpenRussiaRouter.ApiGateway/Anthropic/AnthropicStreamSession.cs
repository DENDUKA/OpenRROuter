using System.Buffers;
using System.Text;
using System.Text.Json;
using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Anthropic;

public sealed class AnthropicStreamSession : IAsyncDisposable
{
    private readonly HttpResponseMessage _response;
    private bool _disposed;

    private static readonly Encoding Utf8 = Encoding.UTF8;

    public AnthropicStreamSession(HttpResponseMessage response)
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
    /// Reads Anthropic SSE events from 9Router and forwards them byte-for-byte / line-for-line to <paramref name="destination"/>.
    /// Preserves all event:, data:, comments, and blank lines.
    /// Accumulates usage according to Anthropic streaming semantics:
    /// - message_start.message.usage provides initial input_tokens, cache tokens
    /// - message_delta.usage provides output_tokens
    /// - _orr_raw is respected if present.
    /// </summary>
    public async Task<TokenUsage> CopyToAsync(Stream destination, CancellationToken cancellationToken)
    {
        var stream = await _response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Utf8, leaveOpen: true);

        long inputTokens = 0;
        long outputTokens = 0;
        long cacheTokens = 0;
        var hasRawUsage = false;

        string? currentEventType = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;

            // Track event types for Anthropic SSE stream
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                currentEventType = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var dataStart = "data:".Length;
                while (dataStart < line.Length && line[dataStart] == ' ')
                    dataStart++;

                var data = line.Substring(dataStart);

                if (data.Contains("\"usage\"", StringComparison.Ordinal))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(data);
                        var root = doc.RootElement;

                        // 1. Check for _orr_raw first
                        if (root.TryGetProperty("usage", out var uElem) && uElem.ValueKind == JsonValueKind.Object &&
                            uElem.TryGetProperty("_orr_raw", out var rawElem) && rawElem.ValueKind == JsonValueKind.Object)
                        {
                            hasRawUsage = true;
                            if (TryReadInt64(rawElem, out var inp, "prompt_tokens", "input_tokens"))
                                inputTokens = inp;
                            if (TryReadInt64(rawElem, out var outp, "completion_tokens", "output_tokens"))
                                outputTokens = outp;
                            if (TryReadInt64(rawElem, out var cr, "cache_read_input_tokens", "cache_tokens", "cached_tokens"))
                                cacheTokens = cr;
                        }
                        else if (root.TryGetProperty("message", out var msgElem) && msgElem.ValueKind == JsonValueKind.Object &&
                                 msgElem.TryGetProperty("usage", out var msgUsageElem) && msgUsageElem.ValueKind == JsonValueKind.Object &&
                                 msgUsageElem.TryGetProperty("_orr_raw", out var msgRawElem) && msgRawElem.ValueKind == JsonValueKind.Object)
                        {
                            hasRawUsage = true;
                            if (TryReadInt64(msgRawElem, out var inp, "prompt_tokens", "input_tokens"))
                                inputTokens = inp;
                            if (TryReadInt64(msgRawElem, out var outp, "completion_tokens", "output_tokens"))
                                outputTokens = outp;
                            if (TryReadInt64(msgRawElem, out var cr, "cache_read_input_tokens", "cache_tokens", "cached_tokens"))
                                cacheTokens = cr;
                        }
                        else if (!hasRawUsage)
                        {
                            // Standard Anthropic usage stream handling
                            if (string.Equals(currentEventType, "message_start", StringComparison.Ordinal) ||
                                (root.TryGetProperty("type", out var typeElem) && typeElem.ValueEquals("message_start")))
                            {
                                if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object &&
                                    msg.TryGetProperty("usage", out var usageElem) && usageElem.ValueKind == JsonValueKind.Object)
                                {
                                    ExtractAnthropicUsageTokens(usageElem, ref inputTokens, ref outputTokens, ref cacheTokens);
                                }
                            }
                            else if (string.Equals(currentEventType, "message_delta", StringComparison.Ordinal) ||
                                     (root.TryGetProperty("type", out var typeElemDelta) && typeElemDelta.ValueEquals("message_delta")))
                            {
                                if (root.TryGetProperty("usage", out var usageElem) && usageElem.ValueKind == JsonValueKind.Object)
                                {
                                    ExtractAnthropicUsageTokens(usageElem, ref inputTokens, ref outputTokens, ref cacheTokens);
                                }
                            }
                            else if (root.TryGetProperty("usage", out var directUsageElem) && directUsageElem.ValueKind == JsonValueKind.Object)
                            {
                                ExtractAnthropicUsageTokens(directUsageElem, ref inputTokens, ref outputTokens, ref cacheTokens);
                            }
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }
            }
            else if (string.IsNullOrWhiteSpace(line))
            {
                currentEventType = null;
            }

            await WriteRawLineAsync(destination, line, cancellationToken).ConfigureAwait(false);
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);

        // Enforce ORR invariant: InputTokens includes CacheTokens
        return new TokenUsage(inputTokens, outputTokens, cacheTokens);
    }

    private static void ExtractAnthropicUsageTokens(JsonElement usageElem, ref long inputTokens, ref long outputTokens, ref long cacheTokens)
    {
        if (TryReadInt64(usageElem, out var inp, "input_tokens", "prompt_tokens"))
        {
            inputTokens = inp;
        }

        if (TryReadInt64(usageElem, out var outp, "output_tokens", "completion_tokens"))
        {
            outputTokens = outp;
        }

        var hasCacheRead = TryReadInt64(usageElem, out var readTokens, "cache_read_input_tokens");
        var hasCacheCreation = TryReadInt64(usageElem, out var creationTokens, "cache_creation_input_tokens");

        if (hasCacheRead || hasCacheCreation)
        {
            cacheTokens = readTokens;
            // Anthropic input_tokens does not include cache_read and cache_creation.
            // ORR invariant requires InputTokens to include all prompt tokens (cache read + cache creation + non-cached input).
            inputTokens += (readTokens + creationTokens);
        }
        else if (TryReadInt64(usageElem, out var directCache, "cache_tokens", "cached_tokens"))
        {
            cacheTokens = directCache;
        }
    }

    private static bool TryReadInt64(JsonElement element, out long value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.TryGetInt64(out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static async Task WriteRawLineAsync(Stream destination, string line, CancellationToken cancellationToken)
    {
        var lineByteCount = Utf8.GetByteCount(line);
        var totalLength = lineByteCount + 1; // + \n
        var buffer = ArrayPool<byte>.Shared.Rent(totalLength);
        try
        {
            Utf8.GetBytes(line, 0, line.Length, buffer, 0);
            buffer[lineByteCount] = (byte)'\n';
            await destination.WriteAsync(buffer.AsMemory(0, totalLength), cancellationToken).ConfigureAwait(false);
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
