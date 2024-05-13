using System.Buffers.Binary;

namespace Currents.Utils;

internal static class Bytes
{
    public unsafe static void Write(byte[] buffer, int start, ushort value)
    {
        unchecked
        {
            fixed (byte* bufferPtr = &buffer[start])
            {
                byte* ptr = bufferPtr;
                *(ushort*)ptr = BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);
            }
        }
    }

    public unsafe static ushort ReadUShort(byte[] buffer, int start)
    {
        unchecked
        {
            fixed (byte* bufferPtr = &buffer[start])
            {
                byte* ptr = bufferPtr;
                return BitConverter.IsLittleEndian ? *(ushort*)ptr : BinaryPrimitives.ReverseEndianness(*(ushort*)ptr);
            }
        }
    }
}
