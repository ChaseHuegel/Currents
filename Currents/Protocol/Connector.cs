using System.Net;
using System.Net.Sockets;
using Currents.Protocol.Packets;
using Currents.Utils;
using Microsoft.Extensions.Logging;

namespace Currents.Protocol;

internal class Connector : IDisposable
{
    public Channel Channel => _channel;
    public bool Connected { get; private set; }

    private Channel _channel;
    private Syn _syn;
    private volatile byte _sequence;
    private readonly PacketConsumer _packetConsumer;
    private readonly HashSet<Connection> _connections = [];
    private readonly CircularBuffer<PacketEvent<Syn>> _synBuffer = new(256);
    private readonly CircularBuffer<PacketEvent<Rst>> _rstBuffer = new(256);
    private readonly Retransmitter?[] _retransmitters = new Retransmitter?[256];
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
        _packetConsumer.Dispose();
        _channel.Dispose();
    }

    public void Close()
    {
        lock (_connections)
        {
            foreach (Connection connection in _connections)
            {
                TryReset(connection);
            }
        }

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
        Syn clientSyn = recv.Packet;

        if (!ValidateServerSyn(clientSyn))
        {
            //  TODO return a ConnectionResult with details instead of bool
            return false;
        }

        _syn = clientSyn;
        Connected = true;
        _logger.LogInformation("{LocalEndPoint} connected to {remoteEndPoint}", _channel.LocalEndPoint, remoteEndPoint);
        _metrics.Connected(_channel.LocalEndPoint, remoteEndPoint);
        return true;
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

    public void Accept()
    {
        Open();

        while (true)
        {
            PacketEvent<Syn> recv = RecvSyn();
            Syn clientSyn = recv.Packet;

            if (!ValidateClientSyn(clientSyn))
            {
                SendRst(recv.EndPoint, clientSyn.Header.Sequence);
                continue;
            }

            var connection = new Connection(recv.EndPoint, clientSyn);
            lock (_connections)
            {
                if (!_connections.Contains(connection))
                {
                    _connections.Add(connection);
                }
            }

            //  TODO Instead of echoing 1:1, construct a syn out of the server's desired params + accepted params from the client's syn
            _syn = clientSyn;
            clientSyn.Header.Controls |= (byte)Packets.Packets.Controls.Ack;
            clientSyn.Header.Ack = clientSyn.Header.Sequence;
            clientSyn.Header.Sequence = _sequence;
            SendSyn(connection, clientSyn, false);
            _logger.LogInformation("Accepted {EndPoint} from {LocalEndPoint} with ack {Ack}", connection, _channel.LocalEndPoint, clientSyn.Header.Ack);
            _metrics.AcceptedConnection(connection, _channel.LocalEndPoint);
            return;
        }
    }

    public bool TryReset(IPEndPoint endPoint)
    {
        lock (_connections)
        {
            if (!_connections.TryGetValue((Connection)endPoint, out Connection connection))
            {
                return false;
            }

            _connections.Remove(connection);

            //  TODO include current ack for the connection
            SendRst(endPoint, 0);
            _logger.LogInformation("Reset {EndPoint} from {LocalEndPoint}", endPoint, _channel.LocalEndPoint);
            return true;
        }
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
        Retransmitter retransmitter = new(_channel, _syn.MaxRetransmissions, _syn.RetransmissionTimeout, segment, endPoint);
        retransmitter.Expired += OnRetransmitterExpired;
        _retransmitters[_sequence] = retransmitter;
    }

    private void OnRetransmitterExpired(object sender, EndPointEventArgs e)
    {
        TryReset(e.EndPoint);
    }

    private void OnAckRecv(object sender, PacketEvent<Ack> e)
    {
        Retransmitter? retransmitter = _retransmitters[e.Packet.Header.Ack];
        _logger.LogInformation("Got an ack for {Ack} {LocalEndPoint} from {EndPoint}", e.Packet.Header.Ack, _channel.LocalEndPoint, e.EndPoint);
        if (retransmitter != null)
        {
            retransmitter.Expired -= OnRetransmitterExpired;
            retransmitter.Dispose();
            _logger.LogInformation("Removed retransmitter for {Ack} {LocalEndPoint}", e.Packet.Header.Ack, _channel.LocalEndPoint);
        }
        _metrics.PacketRecv(Packets.Packets.Controls.Ack, e.Bytes, e.EndPoint, _channel.LocalEndPoint);
    }

    private void OnSynRecv(object sender, PacketEvent<Syn> e)
    {
        _synBuffer.Enqueue(e);
        _metrics.PacketRecv(Packets.Packets.Controls.Syn, e.Bytes, e.EndPoint, _channel.LocalEndPoint);
    }

    private void OnRstRecv(object sender, PacketEvent<Rst> e)
    {
        _rstBuffer.Enqueue(e);
        _metrics.PacketRecv(Packets.Packets.Controls.Rst, e.Bytes, e.EndPoint, _channel.LocalEndPoint);
    }
}