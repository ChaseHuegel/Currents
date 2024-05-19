using System.Net;
using Microsoft.Extensions.Logging;

namespace Currents.Protocol;

public class CrntConnector : IDisposable
{
    public bool Active
    {
        get
        {
            lock (_stateLock)
            {
                return _channel.IsOpen && _consumer.Active;
            }
        }
    }

    private readonly object _stateLock = new();
    private readonly Channel _channel;
    private readonly PacketConsumer _consumer;
    private readonly ILogger _logger;
    private readonly ConnectorMetrics _metrics;
    private readonly List<ConnectionHandler> _connections = [];

    public CrntConnector(IPEndPoint localEndPoint, ILogger logger, ConnectorMetrics metrics)
    {
        _logger = logger;
        _metrics = metrics;

        _channel = new Channel();
        _consumer = new PacketConsumer(_channel);

        _channel.Bind(localEndPoint);
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            lock (_connections)
            {
                foreach (ConnectionHandler connections in _connections)
                {
                    connections.Dispose();
                }

                _connections.Clear();
            }

            Close();
            _consumer.Dispose();
            _channel.Dispose();
        }
    }

    public void Start()
    {
        lock (_stateLock)
        {
            _channel.Open();
            _consumer.Start();
        }
    }

    public void Close()
    {
        lock (_stateLock)
        {
            lock (_connections)
            {
                foreach (ConnectionHandler connection in _connections)
                {
                    connection.Reset();
                }

                _connections.Clear();
            }

            _consumer.Stop();
            _channel.Close();
        }
    }

    public bool TryConnect(IPEndPoint remoteEndPoint)
    {
        return TryConnect(remoteEndPoint, null, out _);
    }

    public bool TryConnect(IPEndPoint remoteEndPoint, out Peer peer)
    {
        return TryConnect(remoteEndPoint, null, out peer);
    }

    public bool TryConnect(IPEndPoint remoteEndPoint, ConnectionParameters? connectionParameters, out Peer peer)
    {
        if (!Active)
        {
            Start();
        }

        var connection = new ConnectionHandler(_channel, _consumer, _logger, _metrics);
        if (connection.TryConnect(remoteEndPoint, connectionParameters, out peer))
        {
            lock (_connections)
            {
                _connections.Add(connection);
            }

            return true;
        }

        peer = null!;
        return false;
    }

    public Peer Accept()
    {
        if (!Active)
        {
            Start();
        }

        var connection = new ConnectionHandler(_channel, _consumer, _logger, _metrics);
        Peer newPeer = connection.Accept();

        lock (_connections)
        {
            _connections.Add(connection);
        }

        return newPeer;
    }

    public void Broadcast(byte[] data)
    {
        lock (_connections)
        {
            for (int i = 0; i < _connections.Count; i++)
            {
                ConnectionHandler connector = _connections[i];
                Peer? peer = connector.Peer;
                if (!connector.Connected || peer == null)
                {
                    continue;
                }

                peer.Send(data);
            }
        }
    }
}
