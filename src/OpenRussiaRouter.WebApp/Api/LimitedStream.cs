namespace OpenRussiaRouter.WebApp.Api;

/// <summary>
/// Stream wrapper that throws <see cref="PayloadTooLargeException"/> once more than
/// <c>maxBytes</c> are read. Used to enforce body size caps when Content-Length may be
/// missing (chunked transfer encoding).
/// </summary>
internal sealed class LimitedStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private long _read;

    public LimitedStream(Stream inner, long maxBytes)
    {
        _inner = inner;
        _maxBytes = maxBytes;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = _inner.Read(buffer, offset, count);
        _read += n;
        if (_read > _maxBytes)
        {
            throw new PayloadTooLargeException();
        }
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await _inner.ReadAsync(buffer, cancellationToken);
        _read += n;
        if (_read > _maxBytes)
        {
            throw new PayloadTooLargeException();
        }
        return n;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class PayloadTooLargeException : Exception;
