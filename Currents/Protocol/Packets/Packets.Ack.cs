using System.Buffers;

namespace Currents.Protocol.Packets;

internal static partial class Packets
{
    public static Ack NewAck(byte sequence, byte ack)
    {
        return new Ack()
        {
            Header = {
                Controls = (byte)Controls.Ack,
                Sequence = sequence,
                Ack = ack
            }
        };
    }

    public static PooledArraySegment<byte> SerializePooledSegment(this Ack packet)
    {
        var segment = new PooledArraySegment<byte>(ArrayPool<byte>.Shared, packet.GetSize());
        packet.SerializeInto(segment.Array, segment.Offset);
        return segment;
    }
}