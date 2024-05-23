using System.Net;
using Currents.Protocol;

namespace Currents.Events;

internal readonly struct PacketEvent<T>(T packet, IPEndPoint endPoint, int bytes, Header header)
{
    public readonly T Packet = packet;
    public readonly IPEndPoint EndPoint = endPoint;
    public readonly int Bytes = bytes;
    public readonly Header Header = header;
}