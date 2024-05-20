using Currents.Protocol.Packets;
using Currents.Utils;

namespace Currents.Protocol;

public class Peer : IEquatable<Peer>, IDisposable
{
    public bool IsConnected => !_disposed && _channel.IsOpen;

    internal readonly Connection Connection;

    private volatile bool _disposed;

    private readonly Channel _channel;
    private readonly CircularBuffer<byte[]> _recvBuffer;

    internal Peer(Connection connection, Channel channel, int bufferSize)
    {
        Connection = connection;
        _channel = channel;
        _recvBuffer = new CircularBuffer<byte[]>(bufferSize);
    }

    public void Dispose()
    {
        _disposed = true;
        _recvBuffer.Dispose();
    }

    public void Send(byte[] data)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Peer));
        }

        if (!IsConnected)
        {
            throw new CrntException($"The {nameof(Peer)} is not connected.");
        }

        //  TODO implement sequencing and acks
        var packet = Packets.Packets.NewAck(0, 0, data);
        var segment = packet.SerializePooledSegment();
        //  TODO support send types (reliable, ordered, sequenced..)
        _channel.Send(segment, Connection.EndPoint);
    }

    public bool TryConsume(out byte[] packet, int timeoutMs = Timeout.Infinite)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Peer));
        }

        if (!IsConnected)
        {
            throw new CrntException($"The {nameof(Peer)} is not connected.");
        }

        return _recvBuffer.TryConsume(out packet, timeoutMs);
    }

    public byte[] Consume()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Peer));
        }

        if (!IsConnected)
        {
            throw new CrntException($"The {nameof(Peer)} is not connected.");
        }

        return _recvBuffer.Consume();
    }

    internal void Produce(byte[] packet)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Peer));
        }

        if (!IsConnected)
        {
            throw new CrntException($"The {nameof(Peer)} is not connected.");
        }

        _recvBuffer.Produce(packet);
    }

    public bool Equals(Peer other)
    {
        return Connection.Equals(other.Connection) && _channel.Equals(other._channel);
    }

    public override bool Equals(object obj)
    {
        if (obj == null)
        {
            return false;
        }

        if (obj is Peer peer)
        {
            return Equals(peer);
        }

        return false;
    }

    public override int GetHashCode()
    {
        return Connection.GetHashCode() * 17 + _channel.GetHashCode();
    }
}