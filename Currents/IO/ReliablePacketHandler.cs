using System.Net;
using Currents.Events;
using Currents.Metrics;
using Currents.Protocol;

namespace Currents.IO;

internal class ReliablePacketHandler : IDisposable
{
    public EventHandler<EndPointEventArgs>? RetransmissionExpired;

    private volatile bool _disposed;
    private volatile byte _sequence;
    private volatile byte _ack;

    private Syn _syn;
    private readonly Channel _channel;
    private readonly PacketConsumer _consumer;
    private readonly ConnectorMetrics _metrics;
    private readonly UnreliablePacketHandler _unreliablePacketHandler;
    private readonly Retransmitter?[] _retransmitters = new Retransmitter?[256];

    public ReliablePacketHandler(UnreliablePacketHandler unreliablePacketHandler, Syn syn, Channel channel, PacketConsumer consumer, ConnectorMetrics metrics)
    {
        _unreliablePacketHandler = unreliablePacketHandler;
        _syn = syn;
        _channel = channel;
        _consumer = consumer;
        _metrics = metrics;

        _sequence = (byte)DateTime.Now.Ticks;
        _ack = syn.Header.Sequence;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        StopListening();

        lock (_retransmitters)
        {
            for (int i = 0; i < _retransmitters.Length; i++)
            {
                Retransmitter? retransmitter = _retransmitters[i];
                if (retransmitter != null)
                {
                    retransmitter.Expired -= RetransmissionExpired;
                    retransmitter.Dispose();
                }
            }
        }

        RetransmissionExpired = null;
    }

    public void StartListening()
    {
        _consumer.AckRecv += OnAckRecv;
        _consumer.DataRecv += OnDataRecv;
    }

    public void StopListening()
    {
        _consumer.AckRecv -= OnAckRecv;
        _consumer.DataRecv -= OnDataRecv;
    }

    public void MergeSyn(Syn syn)
    {
        //  TODO accept negotiable parameters and ignore non-negotiable
        _syn = syn;
        _ack = syn.Header.Sequence;
    }

    public void SendData(byte[] data, IPEndPoint endPoint)
    {
        var packet = Packets.NewAck(_sequence, _ack, Packets.Options.Reliable, data);
        var segment = packet.SerializePooledSegment();

        SendRaw(segment, endPoint);

        _metrics.PacketSent(Packets.Controls.Ack, reliable: true, ordered: false, sequenced: false, bytes: segment.Count, _channel.LocalEndPoint, endPoint);
    }

    public void SendSyn(Syn syn, IPEndPoint endPoint)
    {
        syn.Header.Sequence = _sequence;
        syn.Header.Ack = _ack;

        PooledArraySegment<byte> segment = syn.SerializePooledSegment();
        SendRaw(segment, endPoint);

        _metrics.PacketSent(Packets.Controls.Syn, reliable: true, ordered: false, sequenced: false, bytes: segment.Count, _channel.LocalEndPoint, endPoint);
    }

    public void SendRaw(PooledArraySegment<byte> segment, IPEndPoint endPoint)
    {
        AddRetransmitter(segment, endPoint);
        _sequence++;
        _channel.Send(segment, endPoint);
    }

    public void Ack(PacketEvent<byte[]> e)
    {
        //  TODO acks should be combined with outgoing sends when possible
        byte ack = e.Header.Sequence;
        if (ack < _ack)
        {
            //  TODO implement sliding window
            _ack = ack;
        }

        Ack packet = Packets.NewAck(_sequence, ack, Packets.Options.Reliable);
        PooledArraySegment<byte> segment = packet.SerializePooledSegment();
        _unreliablePacketHandler.SendRaw(segment, e.EndPoint);
    }

    private bool SupportsOptions(Packets.Options options)
    {
        const Packets.Options requiredOptionsMask = ~Packets.Options.Reliable;
        const Packets.Options unallowedOptions = Packets.Options.Sequenced | Packets.Options.Ordered;
        const Packets.Options mask = requiredOptionsMask ^ unallowedOptions;

        Packets.Options maskedOptions = mask | ~options;
        return maskedOptions == requiredOptionsMask;
    }

    private void AddRetransmitter(PooledArraySegment<byte> segment, IPEndPoint endPoint)
    {
        lock (_retransmitters)
        {
            Retransmitter retransmitter = new(_channel, _syn.MaxRetransmissions, _syn.RetransmissionTimeout, segment, endPoint);
            retransmitter.Expired += RetransmissionExpired;
            _retransmitters[_sequence] = retransmitter;
        }
    }

    private void OnAckRecv(object sender, PacketEvent<Ack> e)
    {
        if (!SupportsOptions((Packets.Options)e.Header.Options))
        {
            return;
        }

        lock (_retransmitters)
        {
            Retransmitter? retransmitter = _retransmitters[e.Packet.Header.Ack];
            if (retransmitter != null)
            {
                retransmitter.Expired -= RetransmissionExpired;
                retransmitter.Dispose();
                _retransmitters[e.Packet.Header.Ack] = null;
            }
        }

        _metrics.PacketRecv(Packets.Controls.Ack, e.Bytes, e.EndPoint, _channel.LocalEndPoint);
    }

    private void OnDataRecv(object sender, PacketEvent<byte[]> e)
    {
        if (!SupportsOptions((Packets.Options)e.Header.Options))
        {
            return;
        }

        Ack(e);
    }
}
