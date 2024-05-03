namespace Currents.Protocol.Packets;

internal static partial class Packets
{
    [Flags]
    public enum Controls : byte
    {
        Syn = 0b10000000,
        Ack = 0b01000000,
        Eak = 0b00100000,
        Rst = 0b00010000,
        Nul = 0b00001000,
        Res1 = 0b00000100,
        Res2 = 0b00000010,
        Res3 = 0b00000001
    }
}
