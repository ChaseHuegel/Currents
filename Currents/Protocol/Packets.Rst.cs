using System.Buffers;

namespace Currents.Protocol;

internal static partial class Packets
{
    public static Rst NewRst(byte sequence, byte ack)
    {
        return new Rst()
        {
            Header = {
                Controls = (byte)(Controls.Rst | Controls.Ack),
                Sequence = sequence,
                Ack = ack
            }
        };
    }

    public static PooledArraySegment<byte> SerializePooledSegment(this Rst packet)
    {
        var segment = new PooledArraySegment<byte>(ArrayPool<byte>.Shared, packet.GetSize());
        packet.SerializeInto(segment.Array, segment.Offset);
        return segment;
    }
}
