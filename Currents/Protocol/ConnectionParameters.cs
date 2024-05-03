using Currents.Protocol.Packets;

namespace Currents.Protocol;

internal struct ConnectionParameters
{
    /// <summary>
    /// The version field contains the version of RUDP.  The initial version is one (1).
    /// </summary>
    public byte Version;

    /// <summary>
    /// The maximum number of segments that should be sent without getting an
    /// acknowledgment.  This is used by the receiver as a means of flow control.
    /// The number is selected during connection initiation and may not be
    /// changed later in the life of the connection.  This is not a negotiable
    /// parameter.  Each side must use the value provided by its peer when
    /// sending data.
    /// </summary>
    public byte MaxOutstandingPackets;

    /// <summary>
    /// This field of two octets contains a set of options flags that specify
    /// the set of optional functions that are desired for this connection.
    /// </summary>
    public byte Options;

    /// <summary>
    /// The maximum number of octets that can be received by the peer sending the
    /// SYN segment.  Each peer may specify a different value.  Each peer must not
    /// send packets greater than the value of this field received from its peer
    /// during connection negotiation.  This number includes the size of the RUDP
    /// header. This is not a negotiable parameter.
    /// </summary>
    public ushort MaxPacketSize;

    /// <summary>
    /// The timeout value for retransmission of unacknowledged packets.  This value
    /// is specified in milliseconds. The valid range is 100 to 65536.  This is a
    /// negotiable parameter, both peers must agree on the same value for this
    /// parameter.
    /// </summary>
    public ushort RetransmissionTimeout;

    /// <summary>
    /// The timeout value for sending an acknowledgment segment if another segment
    /// is not sent.  This value is specified in milliseconds. The valid range is
    /// 100 to 65536.  This is a negotiable parameter, both peers must agree on the
    /// same value for this parameter.  In addition, this parameter should be
    /// smaller than the Retransmission Timeout Value.
    /// </summary>
    public ushort CumulativeAckTimeout;

    /// <summary>
    /// The timeout value for sending a null segment if a data segment has not
    /// been sent.  Thus, the null segment acts as a keep-alive mechanism.
    /// This value is specified in milliseconds.  The valid range is 0 to 65536.
    /// A value of 0 disables null segments. This is a negotiable parameter, both
    /// peers must agree on the same value for this parameter.
    /// </summary>
    public ushort NullPacketTimeout;

    /// <summary>
    /// The maximum number of times consecutive retransmission(s) will be attempted
    /// before the connection is considered broken.  The valid range for this value
    /// is 0 to 255.  A value of 0 indicates retransmission should be attempted
    /// forever.  This is a negotiable parameter, both peers must agree on the same
    /// value for this parameter.
    /// </summary>
    public byte MaxRetransmissions;

    /// <summary>
    /// The maximum number of acknowledgments that will be accumulated before
    /// sending an acknowledgment if another segment is not sent. The valid range
    /// for this value is 0 to 255.  A value of 0 indicates an acknowledgment
    /// segment will be send immediately when a data, null, or reset segment is
    /// received.  This is a negotiable parameter, both peers must agree on the
    /// same value for this parameter.
    /// </summary>
    public byte MaxCumulativeAcks;

    /// <summary>
    /// The maximum number of out of sequence packets that will be accumulated
    /// before an EACK (Extended Acknowledgement) segment is sent. The valid range
    /// for this value is 0 to 255.  A value of 0 indicates an EACK will be sent
    /// immediately if an out of order segment is received.  This is a negotiable
    /// parameter, both peers must agree on the same value for this parameter.
    /// </summary>
    public byte MaxOutOfSequencePackets;

    /// <summary>
    /// The maximum number of consecutive auto reset that will performed before
    /// a connection is reset.  The valid range for this value is 0 to 255.  A
    /// value of 0 indicates that an auto reset will not be attempted, the
    /// connection will be reset immediately if an auto reset condition occurs.
    /// This is a negotiable parameter, both peers must agree on the same value
    /// for this parameter.  The consecutive auto reset counter is cleared once
    /// a connection is opened.
    /// </summary>
    public byte MaxAutoResets;

    /// <summary>
    /// Optional security parameters for establishing the connection.
    /// </summary>
    public Sec? Security;
}
