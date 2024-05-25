using System.Net;
using Currents.Events;
using Currents.Metrics;
using Currents.Protocol;

namespace Currents.IO;

internal class UnreliablePacketHandler : IDisposable
{
    public EventHandler<PacketEvent<Rst>>? RstRecv;
    public EventHandler<PacketEvent<byte[]>>? DataRecv;

    private volatile bool _disposed;
    private volatile byte _sequence;

    private readonly Channel _channel;
    private readonly PacketConsumer _consumer;
    private readonly ConnectorMetrics _metrics;

    public UnreliablePacketHandler(Channel channel, PacketConsumer consumer, ConnectorMetrics metrics)
    {
        _channel = channel;
        _consumer = consumer;
        _metrics = metrics;

        _sequence = (byte)DateTime.Now.Ticks;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        StopListening();

        RstRecv = null;
        DataRecv = null;
    }

    public void StartListening()
    {
        _consumer.RstRecv += OnRstRecv;
        _consumer.DataRecv += OnDataRecv;
    }

    public void StopListening()
    {
        _consumer.RstRecv -= OnRstRecv;
        _consumer.DataRecv -= OnDataRecv;
    }

    private bool SupportsOptions(Packets.Options options)
    {
        const Packets.Options requiredOptionsMask = ~Packets.Options.None;
        const Packets.Options unallowedOptions = Packets.Options.Reliable | Packets.Options.Sequenced | Packets.Options.Ordered;
        const Packets.Options mask = requiredOptionsMask ^ unallowedOptions;

        Packets.Options maskedOptions = mask | ~options;
        return maskedOptions == requiredOptionsMask;
    }

    public void SendData(byte[] data, IPEndPoint endPoint)
    {
        var packet = Packets.NewAck(_sequence, 0, Packets.Options.None, data);
        var segment = packet.SerializePooledSegment();

        SendRaw(segment, endPoint);

        _metrics.PacketSent(Packets.Controls.Ack, reliable: true, ordered: false, sequenced: false, bytes: segment.Count, _channel.LocalEndPoint, endPoint);
    }

    public void SendRst(IPEndPoint endPoint)
    {
        Rst packet = Packets.NewRst(0, 0);

        PooledArraySegment<byte> segment = packet.SerializePooledSegment();
        SendRaw(segment, endPoint);

        _metrics.PacketSent(Packets.Controls.Rst, reliable: false, ordered: false, sequenced: false, bytes: segment.Count, _channel.LocalEndPoint, endPoint);
    }

    public void SendRaw(PooledArraySegment<byte> segment, IPEndPoint endPoint)
    {
        _channel.Send(segment, endPoint);
        _sequence++;
    }

    private void OnDataRecv(object sender, PacketEvent<byte[]> e)
    {
        if (!SupportsOptions((Packets.Options)e.Header.Options))
        {
            return;
        }

        DataRecv?.Invoke(this, e);
    }

    private void OnRstRecv(object sender, PacketEvent<Rst> e)
    {
        if (!SupportsOptions((Packets.Options)e.Header.Options))
        {
            return;
        }

        RstRecv?.Invoke(this, e);
    }
}
