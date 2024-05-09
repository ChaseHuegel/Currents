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
    private readonly List<Connection> _connections = [];
    private readonly Retransmitter?[] _retransmitters = new Retransmitter?[256];

    public Connector()
    {
        _channel = new Channel();
    }

    public Connector(IPEndPoint localEndPoint) : this()
    {
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

        _channel.Open();

        SendReliableUnordered(requestedSyn.SerializePooledSegment(), remoteEndPoint);

        RecvEvent recvEvent = ConsumeFromUnordered(remoteEndPoint);

        using (recvEvent.Data)
        {
            Syn responseSyn = Syn.Deserialize(recvEvent.Data.Array, recvEvent.Data.Offset, recvEvent.Data.Count);
            if ((responseSyn.Header.Controls & (byte)Packets.Packets.Controls.Syn) == 0)
            {
                return false;
            }

            if (!ValidateServerSyn(responseSyn))
            {
                //  TODO return a ConnectionResult with details instead of bool
                return false;
            }

            _syn = responseSyn;
            Connected = true;
            return true;
        }
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
                    if (!ValidateClientSyn(syn))
                    {
                        var rst = Packets.Packets.NewRst(_sequence, syn.Header.Sequence);
                        SendUnreliableUnordered(rst.SerializePooledSegment(), recvEvent.EndPoint);
                        continue;
                    }

                    var connection = new Connection(recvEvent.EndPoint, syn);
                    if (!_connections.Contains(connection))
                    {
                        _connections.Add(connection);
                    }

                    //  TODO Instead of echoing 1:1, construct a syn out of the server's desired params + accepted params from the client's syn
                    syn.Header.Controls |= (byte)Packets.Packets.Controls.Ack;
                    syn.Header.Ack = syn.Header.Sequence;
                    syn.Header.Sequence = _sequence;
                    SendUnreliableUnordered(syn.SerializePooledSegment(), connection);
                    return;
                }
            }
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

    private bool ValidateChecksum(RecvEvent recvEvent, Header header)
    {
        //  TODO Introduce a checksum to the header to validate the RecvEvent isn't random junk
        //  TODO It could be random data from the remote that happens to deserialize into a useless object
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
        _channel.Send(segment, endPoint);
        _retransmitters[_sequence] = new Retransmitter(_channel, _syn.MaxRetransmissions, _syn.RetransmissionTimeout, segment, endPoint);
        _sequence++;
    }

    private RecvEvent ConsumeFromUnordered(IPEndPoint remoteEndPoint)
    {
        while (true)
        {
            RecvEvent recvEvent = _channel.ConsumeFrom(remoteEndPoint);
            Header header = Header.Deserialize(recvEvent.Data.Array, recvEvent.Data.Offset, recvEvent.Data.Count);

            if (!ValidateChecksum(recvEvent, header))
            {
                continue;
            }

            if ((header.Controls & (byte)Packets.Packets.Controls.Ack) != 0)
            {
                _retransmitters[header.Ack]?.Dispose();
            }

            return recvEvent;
        }
    }

}