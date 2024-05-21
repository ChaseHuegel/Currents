using System.Diagnostics.Metrics;
using System.Net;
using Currents.Protocol;

namespace Currents.Metrics;

public class ConnectorMetrics
{
    public const string MeterName = "CRNT.Connector";
    public const string RecvPacketMeterName = "crnt.connector.recv_packet";
    public const string RecvBytesMeterName = "crnt.connector.recv_bytes";
    public const string SentPacketMeterName = "crnt.connector.sent_packet";
    public const string SentBytesMeterName = "crnt.connector.sent_bytes";
    public const string ConnectionOpenedMeterName = "crnt.connector.connection.opened";
    public const string ConnectionAcceptedMeterName = "crnt.connector.connection.accepted";

    private readonly Counter<int> _packetRecv;
    private readonly Counter<int> _bytesRecv;
    private readonly Counter<int> _packetSent;
    private readonly Counter<int> _bytesSent;
    private readonly Counter<int> _connectionsOpened;
    private readonly Counter<int> _connectionsAccepted;

    public ConnectorMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _packetRecv = meter.CreateCounter<int>(RecvPacketMeterName);
        _bytesRecv = meter.CreateCounter<int>(RecvBytesMeterName);
        _packetSent = meter.CreateCounter<int>(SentPacketMeterName);
        _bytesSent = meter.CreateCounter<int>(SentBytesMeterName);
        _connectionsOpened = meter.CreateCounter<int>(ConnectionOpenedMeterName);
        _connectionsAccepted = meter.CreateCounter<int>(ConnectionAcceptedMeterName);
    }

    internal void PacketRecv(Packets.Controls type, int bytes, IPEndPoint source, IPEndPoint destination)
    {
        _packetRecv.Add(1, new KeyValuePair<string, object?>("type", type), new KeyValuePair<string, object?>("source", source), new KeyValuePair<string, object?>("destination", destination));
        _bytesRecv.Add(bytes, new KeyValuePair<string, object?>("type", type), new KeyValuePair<string, object?>("source", source), new KeyValuePair<string, object?>("destination", destination));
    }

    internal void PacketSent(Packets.Controls type, bool reliable, bool ordered, bool sequenced, int bytes, IPEndPoint source, IPEndPoint destination)
    {
        _packetSent.Add(1, new KeyValuePair<string, object?>("type", type), new KeyValuePair<string, object?>("reliable", reliable), new KeyValuePair<string, object?>("ordered", ordered), new KeyValuePair<string, object?>("sequenced", sequenced), new KeyValuePair<string, object?>("source", source), new KeyValuePair<string, object?>("destination", destination));
        _bytesSent.Add(bytes, new KeyValuePair<string, object?>("type", type), new KeyValuePair<string, object?>("source", source), new KeyValuePair<string, object?>("destination", destination));
    }

    internal void Connected(IPEndPoint from, IPEndPoint to)
    {
        _connectionsOpened.Add(1, new KeyValuePair<string, object?>("from", from), new KeyValuePair<string, object?>("to", to));
    }

    internal void AcceptedConnection(IPEndPoint from, IPEndPoint to)
    {
        _connectionsAccepted.Add(1, new KeyValuePair<string, object?>("from", from), new KeyValuePair<string, object?>("to", to));
    }
}