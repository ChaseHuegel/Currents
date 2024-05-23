using System.Buffers;

namespace Currents.Protocol;

internal static partial class Packets
{
    public static Ack NewAck(byte sequence, byte ack, Options options, byte[]? data = null)
    {
        return new Ack()
        {
            Header = {
                Controls = (byte)Controls.Ack,
                Sequence = sequence,
                Ack = ack,
                Options = (byte)options
            },
            Data = data
        };
    }

    public static PooledArraySegment<byte> SerializePooledSegment(this Ack packet)
    {
        var segment = new PooledArraySegment<byte>(ArrayPool<byte>.Shared, packet.GetSize());
        packet.SerializeInto(segment.Array, segment.Offset);
        return segment;
    }
}
