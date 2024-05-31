using System.Buffers;

namespace Currents.Protocol;

internal static partial class Packets
{
    public static Nul NewNul(byte sequence, byte ack, Options options, byte[]? data = null)
    {
        return new Nul()
        {
            Header = {
                Controls = (byte)(Controls.Nul | Controls.Ack),
                Sequence = sequence,
                Ack = ack,
                Options = (byte)options
            }
        };
    }

    public static PooledArraySegment<byte> SerializePooledSegment(this Nul packet)
    {
        var segment = new PooledArraySegment<byte>(ArrayPool<byte>.Shared, packet.GetSize());
        packet.SerializeInto(segment.Array, segment.Offset);
        return segment;
    }
}
