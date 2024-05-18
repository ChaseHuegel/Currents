using System.Net;
using Currents.Utils;

namespace Currents.Protocol;

public class Peer : IEquatable<Peer>, IDisposable
{
    public bool IsConnected => _isConnected;

    public readonly Connection Connection;

    private volatile bool _disposed;
    private volatile bool _isConnected;

    private readonly Channel _channel;
    private readonly CircularBuffer<byte[]> _recvBuffer;

    public static implicit operator Connection(Peer client) => client.Connection;
    public static implicit operator IPEndPoint(Peer client) => client.Connection.EndPoint;

    internal Peer(Connection connection, Channel channel, int bufferSize)
    {
        Connection = connection;
        _channel = channel;
        _recvBuffer = new CircularBuffer<byte[]>(bufferSize);
        _isConnected = true;
    }

    public void Dispose()
    {
        _disposed = true;
        _isConnected = false;
        _recvBuffer.Dispose();
    }

    public void Send(PooledArraySegment<byte> segment)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Peer));
        }

        if (!_isConnected)
        {
            throw new CrntException("The client is disconnected.");
        }

        _channel.Send(segment, Connection.EndPoint);
    }

    public bool TryConsume(out byte[] packet, int timeoutMs = Timeout.Infinite)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Peer));
        }

        if (!_isConnected)
        {
            throw new CrntException("The client is disconnected.");
        }

        return _recvBuffer.TryConsume(out packet, timeoutMs);
    }

    public byte[] Consume()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Peer));
        }

        if (!_isConnected)
        {
            throw new CrntException("The client is disconnected.");
        }

        return _recvBuffer.Consume();
    }

    internal void Produce(byte[] packet)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Peer));
        }

        if (!_isConnected)
        {
            throw new CrntException("The client is disconnected.");
        }

        _recvBuffer.Enqueue(packet);
    }

    public bool Equals(Peer other)
    {
        return Connection.Equals(other.Connection);
    }

    public override bool Equals(object obj)
    {
        if (obj == null)
        {
            return false;
        }

        if (obj is Peer client)
        {
            return Equals(client);
        }

        return false;
    }

    public override int GetHashCode()
    {
        return Connection.GetHashCode();
    }
}