using Currents.Protocol.Packets;

namespace Currents.Protocol;

internal class PacketConsumer : IDisposable
{
    public EventHandler<PacketEvent<Syn>>? SynRecv;
    public EventHandler<PacketEvent<Ack>>? AckRecv;
    public EventHandler<PacketEvent<Rst>>? RstRecv;

    private bool _disposed;
    private Thread? _consumeThread;

    private volatile bool _open;

    private readonly Channel _channel;
    private readonly object _stateLock = new();
    private readonly EventWaitHandle _consumeCloseHandle = new(false, EventResetMode.ManualReset);

    public PacketConsumer(Channel channel)
    {
        _channel = channel;
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PacketConsumer));
            }

            if (_open)
            {
                return;
            }

            _open = true;
            _consumeThread = new Thread(ConsumeThread)
            {
                Name = $"CRNT Consume {_channel.LocalEndPoint}"
            };
            _consumeThread.Start();
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (_disposed || !_open)
            {
                return;
            }

            _open = false;
            _consumeCloseHandle.WaitOne();
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed || !_open)
            {
                return;
            }

            _disposed = true;
            Stop();
            SynRecv = null;
            AckRecv = null;
            RstRecv = null;
        }
    }

    public void AddListener<T>(EventHandler<PacketEvent<T>>? listener)
    {
        if (typeof(T) == typeof(Ack))
        {
            AckRecv += listener as EventHandler<PacketEvent<Ack>>;
            return;
        }

        if (typeof(T) == typeof(Syn))
        {
            SynRecv += listener as EventHandler<PacketEvent<Syn>>;
            return;
        }

        if (typeof(T) == typeof(Rst))
        {
            RstRecv += listener as EventHandler<PacketEvent<Rst>>;
            return;
        }
    }

    public void RemoveListener<T>(EventHandler<PacketEvent<T>>? listener)
    {
        if (typeof(T) == typeof(Ack))
        {
            AckRecv -= listener as EventHandler<PacketEvent<Ack>>;
            return;
        }

        if (typeof(T) == typeof(Syn))
        {
            SynRecv -= listener as EventHandler<PacketEvent<Syn>>;
            return;
        }

        if (typeof(T) == typeof(Rst))
        {
            RstRecv -= listener as EventHandler<PacketEvent<Rst>>;
            return;
        }
    }

    private void ConsumeThread()
    {
        _consumeCloseHandle.Reset();

        while (_open)
        {
            if (!_channel.TryConsume(out RecvEvent recvEvent, 50))
            {
                continue;
            }

            using (recvEvent)
            {
                Header header = Header.Deserialize(recvEvent.Data.Array, recvEvent.Data.Offset + 2, recvEvent.Data.Count);

                if ((header.Controls & (byte)Packets.Packets.Controls.Ack) != 0)
                {
                    Ack ack = Ack.Deserialize(recvEvent.Data.Array, recvEvent.Data.Offset, recvEvent.Data.Count);
                    AckRecv?.Invoke(this, new PacketEvent<Ack>(ack, recvEvent.EndPoint));
                }

                if ((header.Controls & (byte)Packets.Packets.Controls.Syn) != 0)
                {
                    Syn syn = Syn.Deserialize(recvEvent.Data.Array, recvEvent.Data.Offset, recvEvent.Data.Count);
                    SynRecv?.Invoke(this, new PacketEvent<Syn>(syn, recvEvent.EndPoint));
                }

                if ((header.Controls & (byte)Packets.Packets.Controls.Rst) != 0)
                {
                    Rst rst = Rst.Deserialize(recvEvent.Data.Array, recvEvent.Data.Offset, recvEvent.Data.Count);
                    RstRecv?.Invoke(this, new PacketEvent<Rst>(rst, recvEvent.EndPoint));
                }
            }
        }

        _consumeCloseHandle.Set();
    }

}
