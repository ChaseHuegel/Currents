using System.Diagnostics;
using System.Security.Cryptography;
using Currents.Protocol;
using Currents.Protocol.Packets;
using Currents.Security.Cryptography;
using NUnit.Framework.Internal;

namespace Currents.Tests.Protocol;

public class ChecksumTests
{
    private readonly int[] TestValues = [3, 1, 10_000, 5, 10];

    [Test]
    public void Syn_Can_Validate()
    {
        var connectionParameters = new ConnectionParameters
        {
            MaxRetransmissions = (byte)TestValues[0],
            Version = (byte)TestValues[1],
            MaxPacketSize = (ushort)TestValues[2],
            MaxOutOfSequencePackets = (byte)TestValues[3],
            MaxOutstandingPackets = (byte)TestValues[4]
        };

        var syn = Packets.NewSyn(connectionParameters);
        var buffer = syn.Serialize();
        ushort sendChecksum = Checksum16.Compute(buffer);
        syn.Header.Checksum = sendChecksum;
        syn.SerializeInto(buffer, 0);

        syn = Syn.Deserialize(buffer, 0, buffer.Length);
        ushort recvChecksum = Checksum16.Compute(buffer);

        Assert.That(syn.Header.Checksum, Is.EqualTo(recvChecksum));
    }

    [Test]
    public void Syn_Check_DistinctPermutations()
    {
        var cases = new ConnectionParameters[TestValues.Length * TestValues.Length];
        int n = 0;
        for (int i = 0; i < cases.Length; i++)
        {
            cases[i] = new ConnectionParameters
            {
                MaxRetransmissions = (byte)TestValues[getWrappedIndex(n)],
                Version = (byte)TestValues[getWrappedIndex(n + 1)],
                MaxPacketSize = (ushort)TestValues[getWrappedIndex(n + 2)],
                MaxOutOfSequencePackets = (byte)TestValues[getWrappedIndex(n + 3)],
                MaxOutstandingPackets = (byte)TestValues[getWrappedIndex(n + 4)]
            };
            n++;
        }

        int getWrappedIndex(int index)
        {
            if (index >= TestValues.Length)
            {
                return (index / TestValues.Length) - 1;
            }

            return index;
        }

        List<ushort> checksums = [];
        foreach (var connectionParameters in cases)
        {
            var syn = Packets.NewSyn(connectionParameters);
            var buffer = syn.Serialize();
            ushort checksum = Checksum16.Compute(buffer);
            checksums.Add(checksum);
        }

        Console.WriteLine($"Distinct checksums: {checksums.Distinct().Count()}/{cases.Length}");
    }

    [Test]
    [TestCase(50)]
    [TestCase(100)]
    [TestCase(250)]
    [TestCase(500)]
    [TestCase(1_000)]
    [TestCase(10_000)]
    [TestCase(100_000)]
    [TestCase(1_000_000)]
    public void Speed_VS_MD5(int size)
    {
        var buffer = new byte[size];

        //  Warmup
        Checksum16.Compute(buffer);
        MD5.HashData(buffer);

        byte n = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = n++;
        }

        Stopwatch checksum16Stopwatch = Stopwatch.StartNew();
        ushort checksum16 = Checksum16.Compute(buffer);
        checksum16Stopwatch.Stop();
        Console.WriteLine($"Checksum16: bytes: {size} checksum: {checksum16} time: {checksum16Stopwatch.ElapsedMilliseconds}ms / {checksum16Stopwatch.ElapsedTicks}t");

        var md5Stopwatch = Stopwatch.StartNew();
        var hash = MD5.HashData(buffer);
        var guid = new Guid(hash);
        md5Stopwatch.Stop();
        Console.WriteLine($"MD5: bytes: {size} checksum: {guid} time: {md5Stopwatch.ElapsedMilliseconds}ms / {md5Stopwatch.ElapsedTicks}t");

        Assert.That(checksum16Stopwatch.ElapsedTicks, Is.LessThan(md5Stopwatch.ElapsedTicks));
    }
}