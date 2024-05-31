using System.Net;
using System.Runtime.CompilerServices;
using System.Timers;
using Currents.Events;
using Currents.Metrics;
using Currents.Protocol;
using Currents.Types;
using Microsoft.Extensions.Logging;

namespace Currents.IO;

internal class PacketIO : IDisposable
{
    public bool IsOpen => !_disposed && _channel.IsOpen;
    public Channel Channel => _channel;
    public CircularBuffer<byte[]> RecvBuffer => _recvBuffer;

    public EventHandler<EndPointEventArgs>? RetransmissionExpired;
    public EventHandler<PacketEvent<Rst>>? RstRcv;

    private volatile bool _disposed;

    private Syn _syn;
    private IPEndPoint _endPoint;
    private readonly Channel _channel;
    private readonly CircularBuffer<byte[]> _recvBuffer;
    private readonly ReliablePacketHandler _reliablePacketHandler;
    private readonly UnreliablePacketHandler _unreliablePacketHandler;
    private readonly OrderedPacketHandler _orderedPacketHandler;
    private readonly System.Timers.Timer _cumulativeAckTimer = new();
    private readonly System.Timers.Timer _nullTimer = new();

    public PacketIO(Connection connection, Channel channel, PacketConsumer consumer, int bufferSize, ILogger logger, ConnectorMetrics metrics)
    {
        _syn = connection.Syn;
        _endPoint = connection.EndPoint;
        _channel = channel;

        _recvBuffer = new CircularBuffer<byte[]>(bufferSize);

        _unreliablePacketHandler = new UnreliablePacketHandler(channel, consumer, metrics);
        _reliablePacketHandler = new ReliablePacketHandler(_unreliablePacketHandler, _syn, channel, consumer, metrics);
        _orderedPacketHandler = new OrderedPacketHandler(_unreliablePacketHandler, _syn, channel, consumer, metrics);

        _cumulativeAckTimer.Elapsed += OnCumulativeAckElapsed;
        _nullTimer.Elapsed += OnNullElapsed;
        UpdateTimerSettings(_syn);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        StopListening();
        RetransmissionExpired = null;
        RstRcv = null;
    }

    public void StartListening()
    {
        _unreliablePacketHandler.RstRecv += OnRstRcv;
        _unreliablePacketHandler.DataRecv += OnDataRecv;
        _unreliablePacketHandler.StartListening();

        _reliablePacketHandler.RetransmissionExpired += OnRetransmissionExpired;
        _reliablePacketHandler.RstRecv += OnRstRcv;
        _reliablePacketHandler.DataRecv += OnDataRecv;
        _reliablePacketHandler.StartListening();

        _orderedPacketHandler.RetransmissionExpired += OnRetransmissionExpired;
        _orderedPacketHandler.RstRecv += OnRstRcv;
        _orderedPacketHandler.DataRecv += OnDataRecv;
        _orderedPacketHandler.StartListening();

        _cumulativeAckTimer.Start();
        if (_syn.NullPacketTimeout > 0)
        {
            _nullTimer.Start();
        }
    }

    public void StopListening()
    {
        _unreliablePacketHandler.RstRecv -= OnRstRcv;
        _unreliablePacketHandler.DataRecv -= OnDataRecv;
        _unreliablePacketHandler.StopListening();

        _reliablePacketHandler.RetransmissionExpired -= RetransmissionExpired;
        _reliablePacketHandler.RstRecv -= OnRstRcv;
        _reliablePacketHandler.DataRecv -= OnDataRecv;
        _reliablePacketHandler.StopListening();

        _orderedPacketHandler.RetransmissionExpired -= RetransmissionExpired;
        _orderedPacketHandler.RstRecv -= OnRstRcv;
        _orderedPacketHandler.DataRecv -= OnDataRecv;
        _orderedPacketHandler.StopListening();

        _cumulativeAckTimer.Stop();
        _nullTimer.Stop();
    }

    public void MergeSyn(Syn syn)
    {
        //  TODO accept negotiable parameters and ignore non-negotiable
        _syn = syn;
        _reliablePacketHandler.MergeSyn(syn);
        _orderedPacketHandler.MergeSyn(syn);
        UpdateTimerSettings(syn);
    }

    public void SendReliable(byte[] data, IPEndPoint endPoint)
    {
        ValidateSendAndThrow(data);
        _reliablePacketHandler.SendData(data, endPoint);
        ResetCumulativeAckTimer();
        ResetNullTimer();
    }

    public void SendOrdered(byte[] data, IPEndPoint endPoint)
    {
        ValidateSendAndThrow(data);
        _orderedPacketHandler.SendData(data, endPoint);
        ResetCumulativeAckTimer();
        ResetNullTimer();
    }

    public void Syn(Syn syn, IPEndPoint endPoint)
    {
        _reliablePacketHandler.SendSyn(syn, endPoint);
        ResetCumulativeAckTimer();
    }

    public void Rst(IPEndPoint endPoint)
    {
        _unreliablePacketHandler.SendRst(endPoint);
        ResetCumulativeAckTimer();
    }

    private void ResetCumulativeAckTimer()
    {
        _cumulativeAckTimer.Stop();
        _cumulativeAckTimer.Start();
    }

    private void ResetNullTimer()
    {
        _nullTimer.Stop();
        if (_syn.NullPacketTimeout > 0)
        {
            _nullTimer.Start();
        }
    }

    private void UpdateTimerSettings(Syn syn)
    {
        _cumulativeAckTimer.Interval = _syn.CumulativeAckTimeout;

        if (_syn.NullPacketTimeout > 0)
        {
            _nullTimer.Interval = _syn.NullPacketTimeout;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateSendAndThrow(byte[] data)
    {
        if (data.Length > _syn.MaxPacketSize - _syn.Header.GetSize())
        {
            throw new CrntException($"Tried to send a packet larger than allowed by the peer's {nameof(Protocol.Syn.MaxPacketSize)} (size: {data.Length} max: {_syn.MaxPacketSize}).");
        }
    }

    private void OnDataRecv(object sender, PacketEvent<byte[]> e)
    {
        if (!e.EndPoint.Equals(_endPoint))
        {
            return;
        }

        RecvBuffer.Produce(e.Packet);
    }

    private void OnRstRcv(object sender, PacketEvent<Rst> e)
    {
        if (!e.EndPoint.Equals(_endPoint))
        {
            return;
        }

        RstRcv?.Invoke(this, e);
    }

    private void OnRetransmissionExpired(object sender, EndPointEventArgs e)
    {
        RetransmissionExpired?.Invoke(this, e);
    }

    private void OnCumulativeAckElapsed(object sender, ElapsedEventArgs e)
    {
        //  TODO only ack if the current offset is out of sync with the current ack
        _reliablePacketHandler.Ack(_endPoint);
        _orderedPacketHandler.Ack(_endPoint);
    }

    private void OnNullElapsed(object sender, ElapsedEventArgs e)
    {
        _reliablePacketHandler.Nul(_endPoint);
    }
}