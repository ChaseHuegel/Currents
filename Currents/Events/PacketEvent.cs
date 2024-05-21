using System.Net;

namespace Currents.Events;

internal readonly struct PacketEvent<T>(T packet, IPEndPoint endPoint, int bytes, byte sequence, byte ack)
{
    public readonly T Packet = packet;
    public readonly IPEndPoint EndPoint = endPoint;
    public readonly int Bytes = bytes;
    public readonly byte Sequence = sequence;
    public readonly byte Ack = ack;
}