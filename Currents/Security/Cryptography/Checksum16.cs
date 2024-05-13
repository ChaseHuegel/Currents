using System.Security.Cryptography;

namespace Currents.Security.Cryptography;

public class Checksum16 : HashAlgorithm
{
    private readonly ushort _seed;
    private ushort _hash;

    public Checksum16()
    {
        _seed = 0;
    }

    public Checksum16(ushort seed)
    {
        _seed = seed;
    }

    public override void Initialize()
    {
    }

    protected override void HashCore(byte[] array, int ibStart, int cbSize)
    {
        _hash = Compute(_seed, array, ibStart, cbSize);
    }

    protected override byte[] HashFinal()
    {
        return ToHashArray(_hash);
    }

    public static byte[] HashData(byte[] buffer)
    {
        return ToHashArray(Compute(0, buffer, 0, buffer.Length));
    }

    public static byte[] HashData(ushort seed, byte[] buffer)
    {
        return ToHashArray(Compute(seed, buffer, 0, buffer.Length));
    }

    public static byte[] HashData(byte[] buffer, int offset, int length)
    {
        return ToHashArray(Compute(0, buffer, offset, length));
    }

    public static byte[] HashData(ushort seed, byte[] buffer, int offset, int length)
    {
        return ToHashArray(Compute(seed, buffer, offset, length));
    }

    public static ushort Compute(byte[] buffer)
    {
        return Compute(0, buffer, 0, buffer.Length);
    }

    public static ushort Compute(ushort seed, byte[] buffer)
    {
        return Compute(seed, buffer, 0, buffer.Length);
    }

    public static ushort Compute(byte[] buffer, int offset, int length)
    {
        return Compute(0, buffer, offset, length);
    }

    public static ushort Compute(ushort seed, byte[] buffer, int offset, int length)
    {
        ushort sum = seed;

        for (int i = offset; i < length; i+=2)
        {
            if (i + 1 >= buffer.Length)
            {
                sum += (ushort)(0 | buffer[i]);
            }
            else
            {
                sum += (ushort)((buffer[i+1] << 8) | buffer[i]);
            }
        }

        return sum;
    }

    private static byte[] ToHashArray(ushort hash)
    {
        if (BitConverter.IsLittleEndian)
        {
            return [(byte)(hash >> 8), (byte)hash];
        }
        else
        {
            return [(byte)hash, (byte)(hash >> 8)];
        }
    }
}