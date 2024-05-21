using System.Net;
using System.Net.Sockets;
using Currents.Events;
using Currents.IO;
using Currents.Metrics;
using Microsoft.Extensions.Logging;

namespace Currents.Protocol;

internal class ConnectionHandler : IDisposable
{
    private const int PacketBufferSize = 256;

    public Channel Channel => _channel;

    public Peer? Peer
    {
        get
        {
            lock (_peerLock)
            {
                return _peer;
            }
        }
    }

    public bool Connected
    {
        get
        {
            lock (_peerLock)
            {
                return _peer != null;
            }
        }
    }

    private volatile Peer? _peer;
    private readonly object _peerLock = new();

    private readonly Channel _channel;
    private readonly PacketConsumer _consumer;
    private readonly ILogger _logger;
    private readonly ConnectorMetrics _metrics;

    public ConnectionHandler(Channel channel, PacketConsumer consumer, ILogger logger, ConnectorMetrics metrics)
    {
        _logger = logger;
        _metrics = metrics;

        _channel = channel;
        _consumer = consumer;
    }

    public void Dispose()
    {
        Reset();
    }

    public void Reset()
    {
        lock (_peerLock)
        {
            if (_peer == null)
            {
                return;
            }

            StopListening();
            _peer.Dispose();
            _peer = null;
        }
    }

    public bool TryConnect(IPEndPoint remoteEndPoint, out Peer peer)
    {
        return TryConnect(remoteEndPoint, null, out peer);
    }

    public bool TryConnect(IPEndPoint remoteEndPoint, ConnectionParameters? connectionParameters, out Peer peer)
    {
        if (!_channel.IsOpen)
        {
            throw new CrntException($"Unable to {nameof(TryConnect)}, the channel is closed.");
        }

        if (_peer != null)
        {
            throw new InvalidOperationException($"An active {typeof(Peer)} must be reset before a new one can be connected to.");
        }

        if (remoteEndPoint.AddressFamily == AddressFamily.InterNetwork)
        {
            remoteEndPoint.Address = remoteEndPoint.Address.MapToIPv6();
        }

        Syn requestedSyn;
        if (connectionParameters != null)
        {
            connectionParameters.Value.ValidateAndThrow();
            requestedSyn = Packets.NewSyn(connectionParameters.Value);
        }
        else
        {
            requestedSyn = Packets.NewSyn();
        }

        lock (_peerLock)
        {
            if (_peer != null)
            {
                throw new InvalidOperationException($"An active {typeof(Peer)} must be reset before a new one can be connected to.");
            }

            var connection = new Connection(remoteEndPoint, requestedSyn);
            _peer = new Peer(connection, _channel, _consumer, PacketBufferSize, _logger, _metrics);
            StartListening();

            _peer.InOut.SendSyn(remoteEndPoint, requestedSyn);

            PacketEvent<Syn> recv = WaitForSyn(remoteEndPoint);
            Syn serverSyn = recv.Packet;

            if (!ValidateServerSyn(serverSyn))
            {
                _peer.InOut.SendRst(remoteEndPoint);
                peer = null!;
                _peer = null;
                return false;
            }

            _peer.InOut.MergeSyn(serverSyn);
            peer = _peer;
        }

        _logger.LogInformation("{LocalEndPoint} connected to {remoteEndPoint}", _channel.LocalEndPoint, remoteEndPoint);
        _metrics.Connected(_channel.LocalEndPoint, remoteEndPoint);
        return true;
    }

    public Peer Accept()
    {
        if (!_channel.IsOpen)
        {
            throw new CrntException($"Unable to {nameof(Accept)}, the channel is closed.");
        }

        if (_peer != null)
        {
            throw new InvalidOperationException($"An active {typeof(Peer)} must be reset before a new one can be accepted.");
        }

        while (_channel.IsOpen)
        {
            lock (_peerLock)
            {
                if (_peer != null)
                {
                    throw new InvalidOperationException($"An active {typeof(Peer)} must be reset before a new one can be accepted.");
                }

                if (!_channel.IsOpen)
                {
                    break;
                }

                PacketEvent<Syn> recv = WaitForSyn();
                Syn clientSyn = recv.Packet;

                var connection = new Connection(recv.EndPoint, clientSyn);
                _peer = new Peer(connection, _channel, _consumer, PacketBufferSize, _logger, _metrics);
                StartListening();

                if (!ValidateClientSyn(clientSyn))
                {
                    _peer.InOut.SendRst(connection);
                    continue;
                }

                //  TODO Instead of echoing 1:1, construct a syn out of the server's desired params + accepted params from the client's syn
                clientSyn.Header.Controls |= (byte)Packets.Controls.Ack;
                clientSyn.Header.Ack = clientSyn.Header.Sequence;
                _peer.InOut.MergeSyn(clientSyn);
                _peer.InOut.SendSyn(connection, clientSyn);

                _logger.LogInformation("Accepted {EndPoint} from {LocalEndPoint} with ack {Ack}", connection, _channel.LocalEndPoint, clientSyn.Header.Ack);
                _metrics.AcceptedConnection(connection, _channel.LocalEndPoint);
                return _peer;
            }
        }

        throw new CrntException($"{nameof(Accept)} interruped, the channel was closed.");
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

    private PacketEvent<Syn> WaitForSyn(IPEndPoint? remoteEndPoint = null)
    {
        if (remoteEndPoint == null)
        {
            return _consumer.SynBuffer.Consume();
        }

        PacketEvent<Syn> syn;
        do
        {
            syn = _consumer.SynBuffer.Consume();
        } while (!syn.EndPoint.Equals(remoteEndPoint));

        return syn;
    }

    private void StartListening()
    {
        lock (_peerLock)
        {
            if (_peer == null)
            {
                return;
            }

            _peer.InOut.StartListening();
            _peer.InOut.RstRcv += OnRstRecv;
            _peer.InOut.RetransmissionExpired += OnRetransmissionExpired;
        }
    }

    private void StopListening()
    {
        lock (_peerLock)
        {
            if (_peer == null)
            {
                return;
            }

            _peer.InOut.StopListening();
            _peer.InOut.RstRcv -= OnRstRecv;
            _peer.InOut.RetransmissionExpired -= OnRetransmissionExpired;
        }
    }

    private void OnRetransmissionExpired(object sender, EndPointEventArgs e)
    {
        _logger.LogError("Connection to {EndPoint} was broken.", e.EndPoint);
        Reset();
    }

    private void OnRstRecv(object sender, PacketEvent<Rst> e)
    {
        _logger.LogWarning("Connection to {EndPoint} was reset by the remote.", e.EndPoint);
        _metrics.PacketRecv(Packets.Controls.Rst, e.Bytes, e.EndPoint, _channel.LocalEndPoint);
        Reset();
    }
}