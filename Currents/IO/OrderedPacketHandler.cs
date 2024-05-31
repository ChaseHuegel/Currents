using System.Net;
using Currents.Events;
using Currents.Metrics;
using Currents.Protocol;
using Currents.Types;

namespace Currents.IO;

internal class OrderedPacketHandler : IDisposable
{
    private readonly struct SendRequest(PooledArraySegment<byte> segment, IPEndPoint endPoint)
    {
        public readonly PooledArraySegment<byte> Segment = segment;
        public readonly IPEndPoint EndPoint = endPoint;
    }

    public EventHandler<PacketEvent<Rst>>? RstRecv;
    public EventHandler<PacketEvent<byte[]>>? DataRecv;
    public EventHandler<EndPointEventArgs>? RetransmissionExpired;

    private volatile bool _disposed;
    private volatile byte _sequence;
    private volatile byte _ack;

    private Syn _syn;
    private readonly Channel _channel;
    private readonly PacketConsumer _consumer;
    private readonly ConnectorMetrics _metrics;
    private readonly UnreliablePacketHandler _unreliablePacketHandler;
    private readonly SlidingWindow<SendRequest> _sendWindow;
    private readonly SlidingWindow<PacketEvent<byte[]>> _recvWindow;
    private readonly Retransmitter?[] _retransmitters = new Retransmitter?[256];

    public OrderedPacketHandler(UnreliablePacketHandler unreliablePacketHandler, Syn syn, Channel channel, PacketConsumer consumer, ConnectorMetrics metrics)
    {
        _unreliablePacketHandler = unreliablePacketHandler;
        _syn = syn;
        _channel = channel;
        _consumer = consumer;
        _metrics = metrics;

        _sequence = 0;
        _ack = syn.Header.Sequence;
        _sendWindow = new SlidingWindow<SendRequest>(syn.MaxOutstandingPackets);
        _recvWindow = new SlidingWindow<PacketEvent<byte[]>>(syn.MaxOutstandingPackets);
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

        RstRecv = null;
        DataRecv = null;
        RetransmissionExpired = null;
    }

    public void StartListening()
    {
        _consumer.AckRecv += OnAckRecv;
        _consumer.RstRecv += OnRstRecv;
        _consumer.DataRecv += OnDataRecv;
        _recvWindow.ItemAccepted += OnRecvAccepted;
        _sendWindow.ItemAvailable += OnSendAvailable;
        _sendWindow.ItemAccepted += OnSendAccepted;
    }

    public void StopListening()
    {
        _consumer.AckRecv -= OnAckRecv;
        _consumer.RstRecv -= OnRstRecv;
        _consumer.DataRecv -= OnDataRecv;
        _recvWindow.ItemAccepted -= OnRecvAccepted;
        _sendWindow.ItemAvailable -= OnSendAvailable;
        _sendWindow.ItemAccepted -= OnSendAccepted;
    }

    public void MergeSyn(Syn syn)
    {
        //  TODO accept negotiable parameters and ignore non-negotiable
        _syn = syn;
        _ack = syn.Header.Sequence;
    }

    public void SendData(byte[] data, IPEndPoint endPoint)
    {
        var packet = Packets.NewAck(_sequence, _ack, Packets.Options.Reliable | Packets.Options.Ordered, data);
        var segment = packet.SerializePooledSegment();
        _sendWindow.TryInsert(_sequence, new SendRequest(segment, endPoint));
        _sequence++;
    }

    public void Ack(byte ack, IPEndPoint endPoint)
    {
        Ack packet = Packets.NewAck(_sequence, ack, Packets.Options.Reliable);
        PooledArraySegment<byte> segment = packet.SerializePooledSegment();
        _unreliablePacketHandler.SendRaw(segment, endPoint);
    }

    public void Ack(IPEndPoint endPoint)
    {
        Ack(_ack, endPoint);
    }

    public void Ack(PacketEvent<byte[]> e)
    {
        //  TODO acks should be combined with outgoing sends when possible
        byte ack = e.Header.Sequence;
        //  TODO implement sliding window
        _ack = ack;

        Ack(ack, e.EndPoint);
    }

    private bool SupportsOptions(Packets.Options options)
    {
        const Packets.Options requiredOptionsMask = ~(Packets.Options.Reliable | Packets.Options.Ordered);
        const Packets.Options unallowedOptions = Packets.Options.Sequenced;
        const Packets.Options mask = requiredOptionsMask ^ unallowedOptions;

        Packets.Options maskedOptions = mask | ~options;
        return maskedOptions == requiredOptionsMask;
    }

    private void AddRetransmitter(byte sequence, PooledArraySegment<byte> segment, IPEndPoint endPoint)
    {
        lock (_retransmitters)
        {
            Retransmitter retransmitter = new(_channel, _syn.MaxRetransmissions, _syn.RetransmissionTimeout, segment, endPoint);
            retransmitter.Expired += OnRetransmissionExpired;
            _retransmitters[sequence] = retransmitter;
        }
    }

    private void OnRetransmissionExpired(object sender, EndPointEventArgs e)
    {
        RetransmissionExpired?.Invoke(this, e);
    }

    private void OnAckRecv(object sender, PacketEvent<Ack> e)
    {
        if (!SupportsOptions((Packets.Options)e.Header.Options))
        {
            return;
        }

        _sendWindow.TryAccept(e.Header.Sequence);
    }

    private void OnDataRecv(object sender, PacketEvent<byte[]> e)
    {
        if (!SupportsOptions((Packets.Options)e.Header.Options))
        {
            return;
        }

        Ack(e);

        _recvWindow.TryInsertAndAccept(e.Header.Sequence, e);
    }

    private void OnRstRecv(object sender, PacketEvent<Rst> e)
    {
        if (!SupportsOptions((Packets.Options)e.Header.Options))
        {
            return;
        }

        RstRecv?.Invoke(this, e);
    }

    private void OnRecvAccepted(object sender, (byte, PacketEvent<byte[]>) e)
    {
        DataRecv?.Invoke(this, e.Item2);
    }

    private void OnSendAvailable(object sender, (byte, SendRequest) e)
    {
        AddRetransmitter(e.Item1, e.Item2.Segment, e.Item2.EndPoint);
        _channel.Send(e.Item2.Segment, e.Item2.EndPoint);
        _metrics.PacketSent(Packets.Controls.Ack, reliable: true, ordered: true, sequenced: false, bytes: e.Item2.Segment.Count, _channel.LocalEndPoint, e.Item2.EndPoint);
    }

    private void OnSendAccepted(object sender, (byte, SendRequest) e)
    {
        lock (_retransmitters)
        {
            Retransmitter? retransmitter = _retransmitters[e.Item1];
            if (retransmitter != null)
            {
                retransmitter.Expired -= OnRetransmissionExpired;
                retransmitter.Dispose();
                _retransmitters[e.Item1] = null;
            }
        }

        _metrics.PacketRecv(Packets.Controls.Ack, e.Item2.Segment.Count, e.Item2.EndPoint, _channel.LocalEndPoint);
    }
}
