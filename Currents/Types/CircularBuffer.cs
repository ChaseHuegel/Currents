namespace Currents.Types;

internal class CircularBuffer<T>(int size) : IDisposable
{
    private volatile int _dequeueIndex;
    private volatile int _enqueueIndex;
    private volatile int _queueSize = 0;

    private readonly object _dequeueLock = new();
    private readonly object _enqueueLock = new();
    private readonly T[] _queue = new T[size];
    private readonly AutoResetEvent _signal = new(false);

    public void Dispose()
    {
        _signal.Dispose();
    }

    public bool TryConsume(out T item, int timeoutMs = Timeout.Infinite)
    {
        lock (_dequeueLock)
        {
            while (_queueSize <= 0)
            {
                _signal.WaitOne(timeoutMs);
            }

            item = Dequeue();
            return true;
        }
    }

    public T Consume()
    {
        lock (_dequeueLock)
        {
            while (_queueSize <= 0)
            {
                _signal.WaitOne();
            }

            return Dequeue();
        }
    }

    public void Produce(T item)
    {
        lock (_enqueueLock)
        {
            _queue[_enqueueIndex] = item;

            if (_enqueueIndex >= _queue.Length - 1)
            {
                _enqueueIndex = 0;
            }
            else
            {
                _enqueueIndex++;
            }
        }

        Interlocked.Increment(ref _queueSize);
        _signal.Set();
    }

    private T Dequeue()
    {
        T item = _queue[_dequeueIndex];
        _queue[_dequeueIndex] = default!;

        if (_dequeueIndex >= _queue.Length - 1)
        {
            _dequeueIndex = 0;
        }
        else
        {
            _dequeueIndex++;
        }

        Interlocked.Decrement(ref _queueSize);
        return item;
    }
}
