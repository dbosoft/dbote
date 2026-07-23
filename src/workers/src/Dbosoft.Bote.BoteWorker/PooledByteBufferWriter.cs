using System.Buffers;

namespace Dbosoft.Bote.BoteWorker;

/// <summary>
/// An <see cref="IBufferWriter{T}"/> backed by <see cref="ArrayPool{T}"/>, so serializing the
/// storage-queue envelope reuses pooled memory instead of allocating a fresh string/byte[] per
/// message. Rent -> write -> read (still inside the using) -> return. Dispose returns the buffer.
/// </summary>
internal sealed class PooledByteBufferWriter(int initialCapacity) : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 256));
    private int _written;

    public int WrittenCount => _written;
    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    public void Advance(int count) => _written += count;

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_written);
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint < 1) sizeHint = 1;
        if (_written + sizeHint <= _buffer.Length) return;

        var newSize = Math.Max(_buffer.Length * 2, _written + sizeHint);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
        Array.Copy(_buffer, newBuffer, _written);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }

    public void Dispose()
    {
        if (_buffer.Length == 0) return;
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = Array.Empty<byte>();
    }
}
