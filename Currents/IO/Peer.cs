using Currents.Metrics;
using Currents.Protocol;
using Microsoft.Extensions.Logging;

namespace Currents.IO;

public class Peer : IEquatable<Peer>, IDisposable
{
    public bool IsConnected => !_disposed && _io.IsOpen;
    internal PacketIO InOut => _io;

    internal readonly Connection Connection;

    private volatile bool _disposed;

    private readonly PacketIO _io;

    internal Peer(Connection connection, Channel channel, PacketConsumer consumer, int bufferSize, ILogger logger, ConnectorMetrics metrics)
    {
        Connection = connection;
        _io = new PacketIO(connection, channel, consumer, bufferSize, logger, metrics);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _io.Rst(Connection.EndPoint);
        _io.Dispose();
    }

    public void Send(byte[] data)
    {
        ValidateAndThrow();

        //  TODO support send types (reliable, ordered, sequenced..)
        _io.SendOrdered(data, Connection.EndPoint);
    }

    public bool TryConsume(out byte[] packet, int timeoutMs = Timeout.Infinite)
    {
        ValidateAndThrow();

        return _io.RecvBuffer.TryConsume(out packet, timeoutMs);
    }

    public byte[] Consume()
    {
        ValidateAndThrow();

        return _io.RecvBuffer.Consume();
    }

    private void ValidateAndThrow()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Peer));
        }

        if (!IsConnected)
        {
            throw new CrntException($"The {nameof(Peer)} is not connected.");
        }
    }

    public bool Equals(Peer other)
    {
        return Connection.Equals(other.Connection) && _io.Channel.Equals(other._io.Channel);
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
        return Connection.GetHashCode() * 17 + _io.Channel.GetHashCode();
    }
}