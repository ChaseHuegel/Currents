using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Currents.Protocol.Packets;

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
    private readonly Retransmitter?[] _retransmitters = new Retransmitter?[256];

    public Connector()
    {
        _channel = new Channel();
        _packetConsumer = new PacketConsumer(_channel);
        _packetConsumer.AckRecv += OnAckRecv;
    }

    public Connector(IPEndPoint localEndPoint) : this()
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

        PooledArraySegment<byte> segment = requestedSyn.SerializePooledSegment();
        SendReliableUnordered(segment, remoteEndPoint);
        Console.WriteLine($"{_channel.LocalEndPoint} sending syn with sequence {requestedSyn.Header.Sequence} and ack {requestedSyn.Header.Ack} to {remoteEndPoint}");

        PacketEvent<Syn> recv = RecvSyn(remoteEndPoint);
        Syn clientSyn = recv.Packet;

        if (!ValidateServerSyn(clientSyn))
        {
            //  TODO return a ConnectionResult with details instead of bool
            return false;
        }

        _syn = clientSyn;
        Connected = true;
        Console.WriteLine($"{_channel.LocalEndPoint} connected to {remoteEndPoint}");
        return true;
    }

    private PacketEvent<Syn> RecvSyn(IPEndPoint? remoteEndPoint = null)
    {
        using PacketSignal<Syn> synRecvSignal = new(_packetConsumer, remoteEndPoint);
        PacketEvent<Syn> serverSyn = synRecvSignal.WaitOne();
        return serverSyn;
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
            clientSyn.Header.Controls |= (byte)Packets.Packets.Controls.Ack;
            clientSyn.Header.Ack = clientSyn.Header.Sequence;
            clientSyn.Header.Sequence = _sequence;
            SendUnreliableUnordered(clientSyn.SerializePooledSegment(), connection);
            Console.WriteLine($"Accepted {connection.EndPoint} from {_channel.LocalEndPoint} with ack {clientSyn.Header.Ack}");
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

    private void SendRst(IPEndPoint endPoint, byte ack)
    {
        Rst rst = Packets.Packets.NewRst(_sequence, ack);
        SendUnreliableUnordered(rst.SerializePooledSegment(), endPoint);
    }

    private void AddRetransmitter(PooledArraySegment<byte> segment, IPEndPoint endPoint)
    {
        Retransmitter retransmitter = new(_channel, _syn.MaxRetransmissions, _syn.RetransmissionTimeout, segment, endPoint);
        retransmitter.Expired += OnRetransmitterExpired;
        _retransmitters[_sequence] = retransmitter;
    }

    private void OnAckRecv(object sender, PacketEvent<Ack> e)
    {
        Retransmitter? retransmitter = _retransmitters[e.Packet.Header.Ack];
        Console.WriteLine($"Got an ack for {e.Packet.Header.Ack} {_channel.LocalEndPoint} from {e.EndPoint}");
        if (retransmitter != null)
        {
            Console.WriteLine($"Removed retransmitter for {e.Packet.Header.Ack} {_channel.LocalEndPoint}");
            retransmitter.Expired -= OnRetransmitterExpired;
            retransmitter.Dispose();
        }
    }

    private void OnRetransmitterExpired(object sender, EndPointEventArgs e)
    {
        Console.WriteLine($"Resetting {e.EndPoint} from {_channel.LocalEndPoint}");
        TryReset(e.EndPoint);
    }
}