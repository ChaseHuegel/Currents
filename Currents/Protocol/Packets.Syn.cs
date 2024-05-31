using System.Buffers;

namespace Currents.Protocol;

internal static partial class Packets
{
    public static Syn NewSyn()
    {
        return new Syn()
        {
            Header = {
                Controls = (byte)Controls.Syn,
                Options = (byte)Options.Reliable,
            },
            Version = 1,
            RetransmissionTimeout = 100,
            MaxRetransmissions = 3,
            MaxOutstandingPackets = 10,
            MaxPacketSize = ushort.MaxValue
        };
    }

    public static Syn NewSyn(ConnectionParameters connectionParameters)
    {
        return new Syn()
        {
            Header = {
                Controls = (byte)Controls.Syn,
                Options = (byte)(Options.Reliable | (Options)connectionParameters.Options),
            },
            Version = connectionParameters.Version,
            MaxOutstandingPackets = connectionParameters.MaxOutstandingPackets,
            MaxPacketSize = connectionParameters.MaxPacketSize,
            RetransmissionTimeout = connectionParameters.RetransmissionTimeout,
            CumulativeAckTimeout = connectionParameters.CumulativeAckTimeout,
            NullPacketTimeout = connectionParameters.NullPacketTimeout,
            MaxRetransmissions = connectionParameters.MaxRetransmissions,
            MaxCumulativeAcks = connectionParameters.MaxCumulativeAcks,
            MaxOutOfSequencePackets = connectionParameters.MaxOutOfSequencePackets,
            MaxAutoResets = connectionParameters.MaxAutoResets,
            Security = connectionParameters.Security
        };
    }

    public static PooledArraySegment<byte> SerializePooledSegment(this Syn packet)
    {
        var segment = new PooledArraySegment<byte>(ArrayPool<byte>.Shared, packet.GetSize());
        packet.SerializeInto(segment.Array, segment.Offset);
        return segment;
    }
}
