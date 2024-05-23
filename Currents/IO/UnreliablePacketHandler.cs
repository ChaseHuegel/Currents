using System.Net;
using Currents.Metrics;
using Currents.Protocol;

namespace Currents.IO;

internal class UnreliablePacketHandler
{
    private volatile byte _sequence;

    private readonly Channel _channel;
    private readonly ConnectorMetrics _metrics;

    public UnreliablePacketHandler(Channel channel, ConnectorMetrics metrics)
    {
        _channel = channel;
        _metrics = metrics;

        _sequence = (byte)DateTime.Now.Ticks;
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
}
