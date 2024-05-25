using System.Net;
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
    private readonly Channel _channel;
    private readonly CircularBuffer<byte[]> _recvBuffer;
    private readonly ReliablePacketHandler _reliablePacketHandler;
    private readonly UnreliablePacketHandler _unreliablePacketHandler;
    private readonly OrderedPacketHandler _orderedPacketHandler;

    public PacketIO(Syn syn, Channel channel, PacketConsumer consumer, int bufferSize, ILogger logger, ConnectorMetrics metrics)
    {
        _syn = syn;
        _channel = channel;

        _recvBuffer = new CircularBuffer<byte[]>(bufferSize);

        _unreliablePacketHandler = new UnreliablePacketHandler(channel, consumer, metrics);
        _reliablePacketHandler = new ReliablePacketHandler(_unreliablePacketHandler, syn, channel, consumer, metrics);
        _orderedPacketHandler = new OrderedPacketHandler(_unreliablePacketHandler, syn, channel, consumer, metrics);
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
        _unreliablePacketHandler.RstRecv += RstRcv;
        _unreliablePacketHandler.DataRecv += OnDataRecv;
        _unreliablePacketHandler.StartListening();

        _reliablePacketHandler.RetransmissionExpired += RetransmissionExpired;
        _reliablePacketHandler.RstRecv += RstRcv;
        _reliablePacketHandler.DataRecv += OnDataRecv;
        _reliablePacketHandler.StartListening();

        _orderedPacketHandler.RetransmissionExpired += RetransmissionExpired;
        _orderedPacketHandler.RstRecv += RstRcv;
        _orderedPacketHandler.DataRecv += OnDataRecv;
        _orderedPacketHandler.StartListening();
    }

    public void StopListening()
    {
        _unreliablePacketHandler.RstRecv -= RstRcv;
        _unreliablePacketHandler.DataRecv -= OnDataRecv;
        _unreliablePacketHandler.StopListening();

        _reliablePacketHandler.RetransmissionExpired -= RetransmissionExpired;
        _reliablePacketHandler.RstRecv -= RstRcv;
        _reliablePacketHandler.DataRecv -= OnDataRecv;
        _reliablePacketHandler.StopListening();

        _orderedPacketHandler.RetransmissionExpired -= RetransmissionExpired;
        _orderedPacketHandler.RstRecv -= RstRcv;
        _orderedPacketHandler.DataRecv -= OnDataRecv;
        _orderedPacketHandler.StopListening();
    }

    public void MergeSyn(Syn syn)
    {
        //  TODO accept negotiable parameters and ignore non-negotiable
        _syn = syn;
        _reliablePacketHandler.MergeSyn(syn);
        _orderedPacketHandler.MergeSyn(syn);
    }

    public void SendReliable(byte[] data, IPEndPoint endPoint)
    {
        _reliablePacketHandler.SendData(data, endPoint);
    }

    public void SendOrdered(byte[] data, IPEndPoint endPoint)
    {
        _orderedPacketHandler.SendData(data, endPoint);
    }

    public void Syn(Syn syn, IPEndPoint endPoint)
    {
        _reliablePacketHandler.SendSyn(syn, endPoint);
    }

    public void Rst(IPEndPoint endPoint)
    {
        _unreliablePacketHandler.SendRst(endPoint);
    }

    private void OnDataRecv(object sender, PacketEvent<byte[]> e)
    {
        RecvBuffer.Produce(e.Packet);
    }
}