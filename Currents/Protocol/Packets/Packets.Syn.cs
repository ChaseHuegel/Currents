namespace Currents.Protocol.Packets;

internal static partial class Packets
{
    public static Syn NewSyn()
    {
        return new Syn()
        {
            Header = {
                Controls = (byte)Controls.Syn
            },
            Version = 1,
            RetransmissionTimeout = 100,
            MaxRetransmissions = 3
        };
    }

    public static Syn NewSyn(ConnectionParameters connectionParameters)
    {
        return new Syn()
        {
            Header = {
                Controls = (byte)Controls.Syn
            },
            Version = connectionParameters.Version,
            MaxOutstandingPackets = connectionParameters.MaxOutstandingPackets,
            Options = connectionParameters.Options,
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
}
