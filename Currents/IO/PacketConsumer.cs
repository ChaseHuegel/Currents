using Currents.Events;
using Currents.Protocol;
using Currents.Types;

namespace Currents.IO;

internal class PacketConsumer : IDisposable
{
    private const int PacketBufferSize = 256;

    public bool Active => _open;

    public EventHandler<PacketEvent<Syn>>? SynRecv;
    public EventHandler<PacketEvent<Ack>>? AckRecv;
    public EventHandler<PacketEvent<Rst>>? RstRecv;
    public EventHandler<PacketEvent<byte[]>>? DataRecv;

    public CircularBuffer<PacketEvent<Ack>> AckBuffer => _ackBuffer;
    public CircularBuffer<PacketEvent<Syn>> SynBuffer => _synBuffer;
    public CircularBuffer<PacketEvent<Rst>> RstBuffer => _rstBuffer;
    public CircularBuffer<PacketEvent<byte[]>> DataBuffer => _dataBuffer;

    private bool _disposed;
    private Thread? _consumeThread;

    private volatile bool _open;

    private readonly Channel _channel;
    private readonly object _stateLock = new();
    private readonly EventWaitHandle _consumeCloseHandle = new(false, EventResetMode.ManualReset);
    private readonly CircularBuffer<PacketEvent<Ack>> _ackBuffer = new(PacketBufferSize);
    private readonly CircularBuffer<PacketEvent<Syn>> _synBuffer = new(PacketBufferSize);
    private readonly CircularBuffer<PacketEvent<Rst>> _rstBuffer = new(PacketBufferSize);
    private readonly CircularBuffer<PacketEvent<byte[]>> _dataBuffer = new(PacketBufferSize);

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
                Packet packet = Packet.Deserialize(recvEvent.Data.Array, recvEvent.Data.Offset, recvEvent.Data.Count);
                Header header = packet.Header;

                if ((header.Controls & (byte)Packets.Controls.Ack) != 0)
                {
                    bool isPiggybackAck = header.Controls != (byte)Packets.Controls.Ack;

                    Ack ack;
                    if (isPiggybackAck)
                    {
                        ack = new Ack(header, null);
                    }
                    else
                    {
                        ack = Ack.Deserialize(recvEvent.Data.Array, recvEvent.Data.Offset, recvEvent.Data.Count);
                    }

                    if (!isPiggybackAck)
                    {
                        if (ack.Data != null && ack.Data.Length > 0)
                        {
                            var dataEvent = new PacketEvent<byte[]>(ack.Data, recvEvent.EndPoint, recvEvent.Data.Count);
                            _dataBuffer.Produce(dataEvent);
                            ScheduleEvent(DataRecv, dataEvent);
                        }
                    }

                    ack.Data = null;
                    var packetEvent = new PacketEvent<Ack>(ack, recvEvent.EndPoint, recvEvent.Data.Count);
                    _ackBuffer.Produce(packetEvent);
                    ScheduleEvent(AckRecv, packetEvent);
                }

                if ((header.Controls & (byte)Packets.Controls.Syn) != 0)
                {
                    Syn syn = Syn.Deserialize(recvEvent.Data.Array, recvEvent.Data.Offset, recvEvent.Data.Count);
                    var packetEvent = new PacketEvent<Syn>(syn, recvEvent.EndPoint, recvEvent.Data.Count);
                    _synBuffer.Produce(packetEvent);
                    ScheduleEvent(SynRecv, packetEvent);
                }

                if ((header.Controls & (byte)Packets.Controls.Rst) != 0)
                {
                    var rst = new Rst(header);
                    var packetEvent = new PacketEvent<Rst>(rst, recvEvent.EndPoint, recvEvent.Data.Count);
                    _rstBuffer.Produce(packetEvent);
                    ScheduleEvent(RstRecv, packetEvent);
                }
            }
        }

        _consumeCloseHandle.Set();
    }

    private void ScheduleEvent<T>(EventHandler<PacketEvent<T>>? eventHandler, PacketEvent<T> packetEvent)
    {
        if (eventHandler == null)
        {
            return;
        }

        Task.Run(InvokeAsync);
        void InvokeAsync()
        {
            eventHandler?.Invoke(this, packetEvent);
        }
    }
}
