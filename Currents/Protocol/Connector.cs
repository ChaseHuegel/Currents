using System.Net;
using System.Net.Sockets;
using Currents.Protocol.Packets;
using Currents.Utils;
using Microsoft.Extensions.Logging;

namespace Currents.Protocol;

internal class Connector : IDisposable
{
    private const int PacketBufferSize = 256;

    public Channel Channel => _channel;
    public bool Connected => _peer != null;

    private Syn _syn;

    private volatile byte _sequence;

    private volatile Peer? _peer;
    private readonly object _peerLock = new();

    private readonly Channel _channel;
    private readonly PacketConsumer _packetConsumer;
    private readonly CircularBuffer<PacketEvent<Syn>> _synBuffer = new(PacketBufferSize);
    private readonly CircularBuffer<PacketEvent<Rst>> _rstBuffer = new(PacketBufferSize);
    private readonly Retransmitter?[] _retransmitters = new Retransmitter?[PacketBufferSize];
    private readonly ILogger _logger;
    private readonly ConnectorMetrics _metrics;

    public Connector(ILogger logger, ConnectorMetrics metrics)
    {
        _logger = logger;
        _metrics = metrics;

        _channel = new Channel();
        _packetConsumer = new PacketConsumer(_channel);

        _packetConsumer.AckRecv += OnAckRecv;
        _packetConsumer.SynRecv += OnSynRecv;
        _packetConsumer.RstRecv += OnRstRecv;
    }

    public Connector(IPEndPoint localEndPoint, ILogger logger, ConnectorMetrics metrics) : this(logger, metrics)
    {
        _channel.Bind(localEndPoint);
    }

    public void Dispose()
    {
        lock (_retransmitters)
        {
            for (int i = 0; i < _retransmitters.Length; i++)
            {
                Retransmitter? retransmitter = _retransmitters[i];
                if (retransmitter != null)
                {
                    retransmitter.Expired -= OnRetransmitterExpired;
                    retransmitter.Dispose();
                }
            }
        }

        _packetConsumer.Dispose();
        _channel.Dispose();
    }

    public void Close()
    {
        Reset();
        _packetConsumer.Stop();
        _channel.Close();
    }


    public bool Connect(IPEndPoint remoteEndPoint, ConnectionParameters? connectionParameters = null)
    {
        if (remoteEndPoint.AddressFamily == AddressFamily.InterNetwork)
        {
            remoteEndPoint.Address = remoteEndPoint.Address.MapToIPv6();
        }

        Syn requestedSyn;
        if (connectionParameters.HasValue)
        {
            connectionParameters.Value.ValidateAndThrow();
            requestedSyn = Packets.Packets.NewSyn(connectionParameters.Value);
        }
        else
        {
            requestedSyn = Packets.Packets.NewSyn();
        }

        _sequence = (byte)DateTime.Now.Ticks;

        requestedSyn.Header.Sequence = _sequence;
        requestedSyn.Options = (byte)Packets.Packets.Options.Reliable;
        _syn = requestedSyn;

        Open();

        SendSyn(remoteEndPoint, requestedSyn, true);
        _logger.LogInformation("{LocalEndPoint} sending syn with sequence {Sequence} and ack {Ack} to {remoteEndPoint}", _channel.LocalEndPoint, requestedSyn.Header.Sequence, requestedSyn.Header.Ack, remoteEndPoint);

        PacketEvent<Syn> recv = RecvSyn(remoteEndPoint);
        Syn serverSyn = recv.Packet;

        if (!ValidateServerSyn(serverSyn))
        {
            //  TODO return a ConnectionResult with details instead of bool
            return false;
        }

        _syn = serverSyn;
        _logger.LogInformation("{LocalEndPoint} connected to {remoteEndPoint}", _channel.LocalEndPoint, remoteEndPoint);
        _metrics.Connected(_channel.LocalEndPoint, remoteEndPoint);
        return true;
    }

    public Peer Accept()
    {
        Open();

        while (_channel.IsOpen)
        {
            PacketEvent<Syn> recv = RecvSyn();
            Syn clientSyn = recv.Packet;

            if (!ValidateClientSyn(clientSyn))
            {
                SendRst(recv.EndPoint, clientSyn.Header.Sequence);
                continue;
            }

            var connection = new Connection(recv.EndPoint, clientSyn);

            lock (_peerLock)
            {
                if (_peer != null)
                {
                    throw new InvalidOperationException($"An active {typeof(Peer)} must be reset before a new one can be accepted.");
                }

                if (!_channel.IsOpen)
                {
                    break;
                }

                _peer = new Peer(connection, _channel, PacketBufferSize);
            }

            //  TODO Instead of echoing 1:1, construct a syn out of the server's desired params + accepted params from the client's syn
            _syn = clientSyn;
            clientSyn.Header.Controls |= (byte)Packets.Packets.Controls.Ack;
            clientSyn.Header.Ack = clientSyn.Header.Sequence;
            clientSyn.Header.Sequence = _sequence;
            SendSyn(connection, clientSyn, false);

            _logger.LogInformation("Accepted {EndPoint} from {LocalEndPoint} with ack {Ack}", connection, _channel.LocalEndPoint, clientSyn.Header.Ack);
            _metrics.AcceptedConnection(connection, _channel.LocalEndPoint);
            return _peer;
        }

        throw new CrntException($"{nameof(Accept)} interruped, the channel was closed.");
    }

    public void Reset()
    {
        Peer? peer = _peer;
        if (peer == null)
        {
            return;
        }

        //  TODO include current ack for the connection
        SendRst(peer.Connection.EndPoint, 0);
        _logger.LogInformation("Reset {EndPoint} from {LocalEndPoint}.", peer.Connection.EndPoint, _channel.LocalEndPoint);
        _peer = null;
    }

    private void Open()
    {
        _channel.Open();
        _packetConsumer.Start();
    }

    private bool ValidateServerSyn(Syn syn)
    {
        //  TODO By default a client will not accept mismatched non-negotiable parameters.
        //  TODO A callback will allow implementors to override this.
        return true;
    }

    private bool ValidateClientSyn(Syn syn)
    {
        //  TODO By default a server will accept non-negotiable parameters.
        //  TODO A callback will allow implementors to override this.
        return true;
    }

    private void SendUnreliableUnordered(PooledArraySegment<byte> segment, IPEndPoint endPoint)
    {
        //  TODO the send methods should handle writing packet headers into the segment
        _channel.Send(segment, endPoint);
        _sequence++;
    }

    private void SendReliableUnordered(PooledArraySegment<byte> segment, IPEndPoint endPoint)
    {
        //  TODO the send methods should handle writing packet headers into the segment
        AddRetransmitter(segment, endPoint);
        _channel.Send(segment, endPoint);
        _sequence++;
    }

    private void SendSyn(IPEndPoint endPoint, Syn syn, bool reliable)
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

        _metrics.PacketSent(Packets.Packets.Controls.Syn, reliable: true, ordered: false, sequenced: false, bytes: segment.Count, _channel.LocalEndPoint, endPoint);
    }

    private void SendRst(IPEndPoint endPoint, byte ack)
    {
        Rst rst = Packets.Packets.NewRst(_sequence, ack);
        PooledArraySegment<byte> segment = rst.SerializePooledSegment();
        SendUnreliableUnordered(segment, endPoint);
        _metrics.PacketSent(Packets.Packets.Controls.Rst, reliable: false, ordered: false, sequenced: false, bytes: segment.Count, _channel.LocalEndPoint, endPoint);
    }

    private void AddRetransmitter(PooledArraySegment<byte> segment, IPEndPoint endPoint)
    {
        lock (_retransmitters)
        {
            Retransmitter retransmitter = new(_channel, _syn.MaxRetransmissions, _syn.RetransmissionTimeout, segment, endPoint);
            retransmitter.Expired += OnRetransmitterExpired;
            _retransmitters[_sequence] = retransmitter;
        }
    }

    private void OnRetransmitterExpired(object sender, EndPointEventArgs e)
    {
        Reset();
    }

    private void OnAckRecv(object sender, PacketEvent<Ack> e)
    {
        _metrics.PacketRecv(Packets.Packets.Controls.Ack, e.Bytes, e.EndPoint, _channel.LocalEndPoint);
        _logger.LogInformation("Got an ack for {Ack} {LocalEndPoint} from {EndPoint}", e.Packet.Header.Ack, _channel.LocalEndPoint, e.EndPoint);

        lock (_retransmitters)
        {
            Retransmitter? retransmitter = _retransmitters[e.Packet.Header.Ack];
            if (retransmitter != null)
            {
                retransmitter.Expired -= OnRetransmitterExpired;
                retransmitter.Dispose();
                _logger.LogInformation("Removed retransmitter for {Ack} {LocalEndPoint}", e.Packet.Header.Ack, _channel.LocalEndPoint);
            }
        }

        if (e.Packet.Data == null || e.Packet.Data.Length <= 0)
        {
            return;
        }

        lock (_peerLock)
        {
            if (_peer == null)
            {
                return;
            }

            _peer.Produce(e.Packet.Data);
        }
    }

    private void OnSynRecv(object sender, PacketEvent<Syn> e)
    {
        _synBuffer.Produce(e);
        _metrics.PacketRecv(Packets.Packets.Controls.Syn, e.Bytes, e.EndPoint, _channel.LocalEndPoint);
    }

    private void OnRstRecv(object sender, PacketEvent<Rst> e)
    {
        _rstBuffer.Produce(e);
        _metrics.PacketRecv(Packets.Packets.Controls.Rst, e.Bytes, e.EndPoint, _channel.LocalEndPoint);
    }

    private PacketEvent<Syn> RecvSyn(IPEndPoint? remoteEndPoint = null)
    {
        if (remoteEndPoint == null)
        {
            return _synBuffer.Consume();
        }

        PacketEvent<Syn> syn;
        do
        {
            syn = _synBuffer.Consume();
        } while (!syn.EndPoint.Equals(remoteEndPoint));

        return syn;
    }
}