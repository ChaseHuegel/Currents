namespace Currents.Types;

public class SlidingWindow<TData> where TData : struct
{
    public EventHandler<(byte, TData)>? ItemAccepted;
    public EventHandler<(byte, TData)>? ItemAvailable;

    private object _lock = new();

    private byte _tail;
    private byte _head;

    private TData?[] _buffer = new TData?[256];

    public SlidingWindow(byte size)
    {
        _head = (byte)(size - 1);
    }

    public SlidingWindow(byte start, byte size)
    {
        _tail = start;
        _head = (byte)(start + size - 1);
    }

    public bool TryInsertAndAccept(byte index, TData item)
    {
        lock (_lock)
        {
            return TryInsertInternal(index, item) && TryAcceptInternal(index);
        }
    }

    public bool TryInsert(byte index, TData item)
    {
        lock (_lock)
        {
            return TryInsertInternal(index, item);
        }
    }

    public bool TryAccept(byte index)
    {
        lock (_lock)
        {
            return TryAcceptInternal(index);
        }
    }

    private bool TryInsertInternal(byte index, TData item)
    {
        if (_buffer[index] != null)
        {
            return false;
        }

        if (index >= _tail || index <= _head)
        {
            ItemAvailable?.Invoke(this, (index, item));
        }

        _buffer[index] = item;
        return true;
    }

    private bool TryAcceptInternal(byte index)
    {
        if (index < _tail || index > _head)
        {
            return false;
        }

        if (index == _tail)
        {
            Slide();
        }

        return true;
    }

    private void Slide()
    {
        do
        {
            TData? tailItem = _buffer[_tail];
            if (tailItem != null)
            {
                ItemAccepted?.Invoke(this, (_tail, tailItem.Value));
                _buffer[_tail] = null;
            }

            _tail++;
            _head++;

            TData? headItem = _buffer[_head];
            if (headItem != null)
            {
                ItemAvailable?.Invoke(this, (_head, headItem.Value));
            }
        } while (_buffer[_tail] != null);
    }
}
