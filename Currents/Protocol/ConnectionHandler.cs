using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Currents.Protocol.Packets;
using Currents.Utils;
using Microsoft.Extensions.Logging;

namespace Currents.Protocol;

internal class ConnectionHandler : IDisposable
{
    private const int PacketBufferSize = 256;

    public Channel Channel => _channel;

    public Peer? Peer
    {
        get
        {
            lock (_peerLock)
            {
                return _peer;
            }
        }
    }

    public bool Connected
    {
        get
        {
            lock (_peerLock)
            {
                return _peer != null;
            }
        }
    }

    private Syn _syn;

    private volatile byte _sequence;

    private volatile Peer? _peer;
    private readonly object _peerLock = new();

    private readonly Channel _channel;
    private readonly PacketConsumer _consumer;
    private readonly Retransmitter?[] _retransmitters = new Retransmitter?[PacketBufferSize];
    private readonly ILogger _logger;
    private readonly ConnectorMetrics _metrics;

    public ConnectionHandler(Channel channel, PacketConsumer consumer, ILogger logger, ConnectorMetrics metrics)
    {
        _logger = logger;
        _metrics = metrics;

        _channel = channel;
        _consumer = consumer;
    }

    public void StartListening()
    {
        StopListening();
        _consumer.AckRecv += OnAckRecv;
        _consumer.RstRecv += OnRstRecv;
        _consumer.DataRecv += OnDataRecv;
    }

    public void StopListening()
    {
        _consumer.AckRecv -= OnAckRecv;
        _consumer.RstRecv -= OnRstRecv;
        _consumer.DataRecv -= OnDataRecv;
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

        StopListening();
    }

    public bool TryConnect(IPEndPoint remoteEndPoint, out Peer peer)
    {
        return TryConnect(remoteEndPoint, null, out peer);
    }

    public bool TryConnect(IPEndPoint remoteEndPoint, ConnectionParameters? connectionParameters, out Peer peer)
    {
        if (remoteEndPoint.AddressFamily == AddressFamily.InterNetwork)
        {
            remoteEndPoint.Address = remoteEndPoint.Address.MapToIPv6();
        }

        Syn requestedSyn;
        if (connectionParameters != null)
        {
            connectionParameters.Value.ValidateAndThrow();
            requestedSyn = Packets.Packets.NewSyn(connectionParameters.Value);
        }
        else
        {
            requestedSyn = Packets.Packets.NewSyn();
        }

        lock (_peerLock)
        {
            _sequence = (byte)DateTime.Now.Ticks;

            requestedSyn.Header.Sequence = _sequence;
            requestedSyn.Options = (byte)Packets.Packets.Options.Reliable;
            _syn = requestedSyn;

            StartListening();
            SendSyn(remoteEndPoint, requestedSyn, true);
            _logger.LogInformation("{LocalEndPoint} sending syn with sequence {Sequence} and ack {Ack} to {remoteEndPoint}", _channel.LocalEndPoint, requestedSyn.Header.Sequence, requestedSyn.Header.Ack, remoteEndPoint);

            PacketEvent<Syn> recv = RecvSyn(remoteEndPoint);
            Syn serverSyn = recv.Packet;

            if (!ValidateServerSyn(serverSyn))
            {
                SendRst(remoteEndPoint, serverSyn.Header.Sequence);
                peer = null!;
                _syn = default;
                _peer = null;
                return false;
            }

            var connection = new Connection(recv.EndPoint, serverSyn);
            _peer = new Peer(connection, _channel, PacketBufferSize);
            _syn = serverSyn;
            peer = _peer;
        }

        _logger.LogInformation("{LocalEndPoint} connected to {remoteEndPoint}", _channel.LocalEndPoint, remoteEndPoint);
        _metrics.Connected(_channel.LocalEndPoint, remoteEndPoint);
        return true;
    }

    public Peer Accept()
    {
        if (!_channel.IsOpen)
        {
            throw new CrntException($"Unable to {nameof(Accept)}, the channel is closed.");
        }

        if (_peer != null)
        {
            throw new InvalidOperationException($"An active {typeof(Peer)} must be reset before a new one can be accepted.");
        }

        StartListening();

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
        }

        throw new CrntException($"{nameof(Accept)} interruped, the channel was closed.");
    }

    public void Reset()
    {
        lock (_peerLock)
        {
            if (_peer == null)
            {
                return;
            }

            StopListening();

            //  TODO include current ack for the connection
            SendRst(_peer.Connection.EndPoint, 0);
            _peer.Dispose();
            _logger.LogInformation("Reset {EndPoint} from {LocalEndPoint}.", _peer.Connection.EndPoint, _channel.LocalEndPoint);

            _peer = null;
        }
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
        lock (_peerLock)
        {
            if (_peer == null)
            {
                return;
            }

            if (!e.EndPoint.Equals(_peer.Connection.EndPoint))
            {
                return;
            }

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
        }
    }

    private void OnRstRecv(object sender, PacketEvent<Rst> e)
    {
        lock (_peerLock)
        {
            if (_peer == null)
            {
                return;
            }

            if (!e.EndPoint.Equals(_peer.Connection.EndPoint))
            {
                return;
            }

            _metrics.PacketRecv(Packets.Packets.Controls.Rst, e.Bytes, e.EndPoint, _channel.LocalEndPoint);
            Reset();
        }
    }

    private void OnDataRecv(object sender, PacketEvent<byte[]> e)
    {
        lock (_peerLock)
        {
            if (_peer == null)
            {
                return;
            }

            if (e.Packet == null || e.Packet.Length <= 0)
            {
                return;
            }

            _peer.Produce(e.Packet);
        }
    }

    private PacketEvent<Syn> RecvSyn(IPEndPoint? remoteEndPoint = null)
    {
        if (remoteEndPoint == null)
        {
            return _consumer.SynBuffer.Consume();
        }

        PacketEvent<Syn> syn;
        do
        {
            syn = _consumer.SynBuffer.Consume();
        } while (!syn.EndPoint.Equals(remoteEndPoint));

        return syn;
    }
}