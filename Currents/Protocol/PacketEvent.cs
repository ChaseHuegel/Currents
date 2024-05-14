using System.Net;

namespace Currents.Protocol;

internal readonly struct PacketEvent<T>(T packet, IPEndPoint endPoint)
{
    public readonly T Packet = packet;
    public readonly IPEndPoint EndPoint = endPoint;
}