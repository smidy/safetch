using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Safetch.Core.Http;

/// <summary>
/// Wraps a Stream and throws ResponseTooLargeException once the total bytes read exceeds the limit.
/// </summary>
public sealed class LengthLimitedStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private long _total;

    public LengthLimitedStream(Stream inner, long limit)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _limit = limit;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        _total += read;
        if (_total > _limit) throw new ResponseTooLargeException();
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var read = await _inner.ReadAsync(buffer, ct);
        _total += read;
        if (_total > _limit) throw new ResponseTooLargeException();
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => ReadAsync(new Memory<byte>(buffer, offset, count), ct).AsTask();

    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}