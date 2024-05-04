using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Currents.Protocol.Packets;

namespace Currents.Protocol;

internal class Connector : IDisposable
{
    public Channel Channel => _channel;

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
        _channel = new Channel();
        _syn = Packets.Packets.NewSyn(connectionParameters);
    }

    public Connector(IPEndPoint localEndPoint, ConnectionParameters connectionParameters) : this(connectionParameters)
    {
        _channel.Bind(localEndPoint);
    }

    public void Dispose()
    {
        _channel.Dispose();
    }

    public int ConnectionAttempts = 1;
    public bool Connected = false;

    public bool Connect(IPEndPoint remoteEndPoint)
    {
        _channel.Open();

        if (remoteEndPoint.AddressFamily == AddressFamily.InterNetwork)
        {
            remoteEndPoint.Address = remoteEndPoint.Address.MapToIPv6();
        }

        byte[] buffer = _syn.Serialize();
        _channel.Send(buffer, remoteEndPoint);

        bool retransmit = true;
        Task.Run(() => Retransmit(buffer, remoteEndPoint));
        async Task Retransmit(byte[] data, IPEndPoint endPoint)
        {
            while (retransmit)
            {
                await Task.Delay(100);
                _channel.Send(data, endPoint);
                ConnectionAttempts++;
            }
        }

        DataReceivedEvent recvEvent = RecvFrom(remoteEndPoint);
        using (recvEvent.Data)
        {
            Syn remoteSyn = Syn.Deserialize(recvEvent.Data.Array, recvEvent.Data.Offset, recvEvent.Data.Count);

            if ((remoteSyn.Header.Controls & (byte)Packets.Packets.Controls.Syn) != 0)
            {
                //  TODO validate and accept or decline the syn
                _syn = remoteSyn;
                retransmit = false;
                Connected = true;
                return true;
            }
        }

        retransmit = false;
        return false;
    }

    public void Accept()
    {
        _channel.Open();
        while (true)
        {
            DataReceivedEvent recvEvent = _channel.Consume();
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

                    _channel.Send(syn.Serialize(), connection);
                    return;
                }
            }
        }
    }

    private DataReceivedEvent RecvFrom(IPEndPoint targetEndPoint)
    {
        while (true)
        {
            DataReceivedEvent recvEvent = _channel.Consume();
            if (targetEndPoint.Equals(recvEvent.EndPoint))
            {
                return recvEvent;
            }
        }
    }

}