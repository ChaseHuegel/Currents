using System.Net;
using System.Net.Sockets;
using Currents.Protocol.Packets;

namespace Currents.Protocol;

internal class Connector : IDisposable
{
    public Socket Socket => _socket;

    private Socket _socket;
    private Syn _syn;
    private readonly List<Connection> _connections = [];

    private Connector(bool ipv6Only)
    {
        _socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, ipv6Only);
    }

    public Connector() : this(ipv6Only: false)
    {
        _syn = Packets.Packets.NewSyn();
    }

    public Connector(IPEndPoint localEndPoint) : this()
    {
        Bind(localEndPoint);
    }

    public Connector(ConnectionParameters connectionParameters) : this(ipv6Only: false)
    {
        _syn = Packets.Packets.NewSyn(connectionParameters);
    }

    public Connector(IPEndPoint localEndPoint, ConnectionParameters connectionParameters) : this(connectionParameters)
    {
        Bind(localEndPoint);
    }

    public void Dispose()
    {
        _socket.Dispose();
    }

    public void Bind(IPEndPoint localEndPoint)
    {
        _socket.Bind(localEndPoint);
    }

    public int ConnectionAttempts = 1;
    public bool Connected = false;

    public bool Connect(IPEndPoint remoteEndPoint)
    {
        if (remoteEndPoint.AddressFamily == AddressFamily.InterNetwork)
        {
            remoteEndPoint.Address = remoteEndPoint.Address.MapToIPv6();
        }

        byte[] buffer = _syn.Serialize();
        _socket.SendTo(buffer, remoteEndPoint);

        bool retransmit = true;
        Task.Run(() => Retransmit(buffer, remoteEndPoint));
        async Task Retransmit(byte[] data, IPEndPoint endPoint)
        {
            while (retransmit)
            {
                await Task.Delay(100);
                _socket.SendTo(data, endPoint);
                ConnectionAttempts++;
            }
        }

        var recvBuffer = new byte[256];
        ArraySegment<byte> recvBytes = RecvFrom(recvBuffer, remoteEndPoint);
        Syn remoteSyn = Syn.Deserialize(recvBytes.Array, recvBytes.Offset, recvBytes.Count);

        if ((remoteSyn.Header.Controls & (byte)Packets.Packets.Controls.Syn) != 0)
        {
            //  TODO validate and accept or decline the syn
            _syn = remoteSyn;
            retransmit = false;
            Connected = true;
            return true;
        }

        retransmit = false;
        return false;
    }

    public void Accept()
    {
        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        byte[] buffer = new byte[256];
        while (true)
        {
            ArraySegment<byte> recvBytes = Recv(buffer, ref remoteEndPoint);
            Connection connection = (IPEndPoint)remoteEndPoint;

            Syn syn = Syn.Deserialize(recvBytes.Array, recvBytes.Offset, recvBytes.Count);

            if ((syn.Header.Controls & (byte)Packets.Packets.Controls.Syn) != 0)
            {
                //  TODO validate and choose to accept or decline the syn
                if (!_connections.Contains(connection))
                {
                    _connections.Add(connection);
                }

                _socket.SendTo(syn.Serialize(), connection);
                return;
            }
        }
    }

    private ArraySegment<byte> Recv(ArraySegment<byte> buffer, ref EndPoint remoteEndPoint)
    {
        while (true)
        {
            int bytesRec = _socket.ReceiveFrom(buffer.Array, buffer.Offset, buffer.Count, SocketFlags.None, ref remoteEndPoint);

            if (bytesRec > 0)
            {
                return new ArraySegment<byte>(buffer.Array, buffer.Offset, bytesRec);
            }
        }
    }

    private ArraySegment<byte> RecvFrom(ArraySegment<byte> buffer, IPEndPoint targetEndPoint)
    {
        EndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            int bytesRec = _socket.ReceiveFrom(buffer.Array, buffer.Offset, buffer.Count, SocketFlags.None, ref endPoint);

            if (targetEndPoint.Equals(endPoint) && bytesRec > 0)
            {
                return new ArraySegment<byte>(buffer.Array, buffer.Offset, bytesRec);
            }
        }
    }

}