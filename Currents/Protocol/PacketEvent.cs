using System.Net;

namespace Currents.Protocol;

internal readonly struct PacketEvent<T>(T packet, IPEndPoint endPoint, int bytes)
{
    public readonly T Packet = packet;
    public readonly IPEndPoint EndPoint = endPoint;
    public readonly int Bytes = bytes;
}