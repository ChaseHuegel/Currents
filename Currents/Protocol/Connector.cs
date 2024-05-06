using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Currents.Protocol.Packets;

namespace Currents.Protocol;

internal class Connector : IDisposable
{
    public Channel Channel => _channel;
    public int ConnectionAttempts { get; private set; }
    public bool Connected { get; private set; }

    private Channel _channel;
    private Syn _syn;
    private readonly List<Connection> _connections = [];

    public Connector()
    {
        _channel = new Channel();
        _syn = Packets.Packets.NewSyn();
    }

    public Connector(IPEndPoint localEndPoint) : this()
    {
        _channel.Bind(localEndPoint);
    }

    public Connector(ConnectionParameters connectionParameters)
    {
        connectionParameters.ValidateAndThrow();
        _channel = new Channel();
        _syn = Packets.Packets.NewSyn(connectionParameters);
    }

    public Connector(IPEndPoint localEndPoint, ConnectionParameters connectionParameters) : this(connectionParameters)
    {
        connectionParameters.ValidateAndThrow();
        _channel.Bind(localEndPoint);
    }

    public void Dispose()
    {
        _channel.Dispose();
    }

    public void Close()
    {
        _channel.Close();
    }

    public bool Connect(IPEndPoint remoteEndPoint)
    {
        _channel.Open();

        if (remoteEndPoint.AddressFamily == AddressFamily.InterNetwork)
        {
            remoteEndPoint.Address = remoteEndPoint.Address.MapToIPv6();
        }

        var synSegment = new PooledArraySegment<byte>(ArrayPool<byte>.Shared, _syn.GetSize());
        _syn.SerializeInto(synSegment.Array, synSegment.Offset);
        _channel.Send(synSegment, remoteEndPoint);

        using var synRetransmitter = new Retransmitter(_channel, _syn.MaxRetransmissions, _syn.RetransmissionTimeout, synSegment, remoteEndPoint);

        RecvEvent recvEvent = _channel.ConsumeFrom(remoteEndPoint);
        using (recvEvent.Data)
        {
            Syn remoteSyn = Syn.Deserialize(recvEvent.Data.Array, recvEvent.Data.Offset, recvEvent.Data.Count);

            if ((remoteSyn.Header.Controls & (byte)Packets.Packets.Controls.Syn) != 0)
            {
                //  TODO validate and accept or decline the syn
                _syn = remoteSyn;
                ConnectionAttempts = synRetransmitter.Retransmissions + 1;
                Connected = true;
                return true;
            }
        }

        return false;
    }

    public void Accept()
    {
        _channel.Open();

        while (true)
        {
            RecvEvent recvEvent = _channel.Consume();
            using (recvEvent.Data)
            {
                Syn syn = Syn.Deserialize(recvEvent.Data.Array, recvEvent.Data.Offset, recvEvent.Data.Count);

                if ((syn.Header.Controls & (byte)Packets.Packets.Controls.Syn) != 0)
                {
                    var connection = new Connection(recvEvent.EndPoint);

                    //  TODO validate and choose to accept or decline the syn
                    if (!_connections.Contains(connection))
                    {
                        _connections.Add(connection);
                    }

                    var segment = new PooledArraySegment<byte>(ArrayPool<byte>.Shared, syn.GetSize());
                    syn.SerializeInto(segment.Array, segment.Offset);
                    _channel.Send(segment, connection);
                    return;
                }
            }
        }
    }

}