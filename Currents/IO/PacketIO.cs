using System.Net;
using Currents.Events;
using Currents.Metrics;
using Currents.Protocol;
using Currents.Types;
using Microsoft.Extensions.Logging;

namespace Currents.IO;

internal class PacketIO : IDisposable
{
    public bool IsOpen => !_disposed && _channel.IsOpen;
    public Channel Channel => _channel;
    public CircularBuffer<byte[]> RecvBuffer => _recvBuffer;

    public EventHandler<EndPointEventArgs>? RetransmissionExpired;
    public EventHandler<PacketEvent<Rst>>? RstRcv;

    private volatile bool _disposed;
    private volatile byte _sequence;
    private volatile byte _ack;

    private Syn _syn;
    private readonly Channel _channel;
    private readonly PacketConsumer _consumer;
    private readonly ILogger _logger;
    private readonly ConnectorMetrics _metrics;
    private readonly Retransmitter?[] _retransmitters = new Retransmitter?[256];
    private readonly CircularBuffer<byte[]> _recvBuffer;

    public PacketIO(Syn syn, Channel channel, PacketConsumer consumer, int bufferSize, ILogger logger, ConnectorMetrics metrics)
    {
        _syn = syn;
        _channel = channel;
        _consumer = consumer;
        _logger = logger;
        _metrics = metrics;

        _sequence = (byte)DateTime.Now.Ticks;
        _recvBuffer = new CircularBuffer<byte[]>(bufferSize);
        _ack = syn.Header.Sequence;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

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
        RstRcv = null;
    }

    public void StartListening()
    {
        _consumer.AckRecv += OnAckRecv;
        _consumer.DataRecv += OnDataRecv;
        _consumer.RstRecv += RstRcv;
    }

    public void StopListening()
    {
        _consumer.AckRecv -= OnAckRecv;
        _consumer.DataRecv -= OnDataRecv;
        _consumer.RstRecv -= RstRcv;
    }

    public void MergeSyn(Syn syn)
    {
        //  TODO accept negotiable parameters and ignore non-negotiable
        _syn = syn;
    }

    public void Send(byte[] data, IPEndPoint endPoint)
    {
        var packet = Packets.NewAck(_sequence, _ack, data);
        var segment = packet.SerializePooledSegment();

        //  TODO support send types (reliable, ordered, sequenced..)
        SendUnreliableUnordered(segment, endPoint);
    }

    public void SendUnreliableUnordered(PooledArraySegment<byte> segment, IPEndPoint endPoint)
    {
        //  TODO the send methods should handle writing packet headers into the segment
        _channel.Send(segment, endPoint);
        _sequence++;
    }

    public void SendReliableUnordered(PooledArraySegment<byte> segment, IPEndPoint endPoint)
    {
        //  TODO the send methods should handle writing packet headers into the segment
        AddRetransmitter(segment, endPoint);
        _channel.Send(segment, endPoint);
        _sequence++;
    }

    public void SendSyn(IPEndPoint endPoint, Syn syn)
    {
        syn.Header.Sequence = _sequence;
        syn.Header.Ack = _ack;

        PooledArraySegment<byte> segment = syn.SerializePooledSegment();
        SendUnreliableUnordered(segment, endPoint);

        _metrics.PacketSent(Packets.Controls.Syn, reliable: true, ordered: false, sequenced: false, bytes: segment.Count, _channel.LocalEndPoint, endPoint);
    }

    public void SendRst(IPEndPoint endPoint, byte? ack = null)
    {
        Rst rst = Packets.NewRst(_sequence, ack ?? _ack);

        PooledArraySegment<byte> segment = rst.SerializePooledSegment();
        SendUnreliableUnordered(segment, endPoint);

        _metrics.PacketSent(Packets.Controls.Rst, reliable: false, ordered: false, sequenced: false, bytes: segment.Count, _channel.LocalEndPoint, endPoint);
    }

    public void SendSyn(IPEndPoint endPoint, Syn syn, bool reliable)
    {
        PooledArraySegment<byte> segment = syn.SerializePooledSegment();
        if (reliable)
        {
            SendReliableUnordered(segment, endPoint);
        }
        else
        {
            SendUnreliableUnordered(segment, endPoint);
        }

        _metrics.PacketSent(Packets.Controls.Syn, reliable: true, ordered: false, sequenced: false, bytes: segment.Count, _channel.LocalEndPoint, endPoint);
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
        _metrics.PacketRecv(Packets.Controls.Ack, e.Bytes, e.EndPoint, _channel.LocalEndPoint);

        lock (_retransmitters)
        {
            Retransmitter? retransmitter = _retransmitters[e.Packet.Header.Ack];
            if (retransmitter != null)
            {
                retransmitter.Expired -= RetransmissionExpired;
                retransmitter.Dispose();
            }
        }
    }

    private void OnDataRecv(object sender, PacketEvent<byte[]> e)
    {
        RecvBuffer.Produce(e.Packet);
    }

}
