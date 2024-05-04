using System.Buffers;

namespace Currents.Protocol;

public struct PooledArraySegment<T>(ArrayPool<T> pool, T[] array, int offset, int count) : IDisposable
{
    public readonly T[] Array { get => _disposed ? throw new ObjectDisposedException(nameof(PooledArraySegment<T>)) : _array; }
    public readonly int Offset { get => _disposed ? throw new ObjectDisposedException(nameof(PooledArraySegment<T>)) : _offset; }
    public readonly int Count { get => _disposed ? throw new ObjectDisposedException(nameof(PooledArraySegment<T>)) : _count; }

    private readonly T[] _array = array;
    private readonly int _offset = offset;
    private readonly int _count = count;
    private readonly ArrayPool<T> _arrayPool = pool;
    private bool _disposed;

    public PooledArraySegment(ArrayPool<T> pool, int length)
        : this(pool, pool.Rent(length), 0, length) { }

    public PooledArraySegment(ArrayPool<T> pool, T[] array, int count)
        : this(pool, array, 0, count) { }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            if (_array != null)
            {
                _arrayPool.Return(_array);
            }
        }
    }

    public readonly T this[int index]
    {
        get
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PooledArraySegment<T>));
            }

            if (index < 0 || index >= _offset + _count)
            {
                throw new IndexOutOfRangeException();
            }

            return _array[_offset + index];
        }
        set
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PooledArraySegment<T>));
            }

            if (index < 0 || index >= _offset + _count)
            {
                throw new IndexOutOfRangeException();
            }

            _array[_offset + index] = value;
        }
    }

}
