namespace Currents.Types;

public class SlidingWindow<TData> where TData : struct
{
    public EventHandler<(byte, TData)>? Accepted;
    public EventHandler<(byte, TData)>? Available;

    private object _lock = new();

    private byte _tail;
    private byte _head;

    private TData?[] _buffer = new TData?[256];

    public SlidingWindow(byte size)
    {
        _head = size;
    }

    public bool TryInsert(byte index, TData data)
    {
        lock (_lock)
        {
            if (_buffer[index] != null)
            {
                return false;
            }

            if (index >= _tail || index <= _head)
            {
                Available?.Invoke(this, (index, data));
            }

            _buffer[index] = data;
            return true;
        }
    }

    public bool TryAccept(byte index)
    {
        lock (_lock)
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
    }

    private void Slide()
    {
        do
        {
            TData? tailItem = _buffer[_tail];
            if (tailItem != null)
            {
                Accepted?.Invoke(this, (_tail, tailItem.Value));
                _buffer[_tail] = null;
            }

            _tail++;
            _head++;

            TData? headItem = _buffer[_head];
            if (headItem != null)
            {
                Available?.Invoke(this, (_head, headItem.Value));
            }
        } while (_buffer[_tail] != null);
    }
}
