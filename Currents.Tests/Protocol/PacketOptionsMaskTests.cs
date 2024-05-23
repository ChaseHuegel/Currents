using Options = Currents.Protocol.Packets.Options;

namespace Currents.Tests.Protocol;

internal class PacketOptionsMaskTests
{
    [Test]
    [TestCase(Options.Reliable | Options.Sequenced, (byte)0, Options.Reliable | Options.Sequenced | Options.Ordered | Options.Res2)]
    [TestCase(Options.Reliable | Options.Sequenced | Options.Ordered, (byte)0, Options.Reliable | Options.Sequenced | Options.Ordered | Options.Res2)]
    [TestCase(Options.Reliable, Options.Sequenced | Options.Ordered, Options.Reliable | Options.Res2)]
    [TestCase((byte)0, Options.Reliable | Options.Sequenced | Options.Ordered, Options.Res2)]
    public void Options_Are_Valid(Options required, Options unallowed, Options options)
    {
        bool result = CheckMask(required, unallowed, options);
        Assert.That(result, Is.True);
    }

    [Test]
    [TestCase(Options.Reliable, Options.Sequenced | Options.Ordered, Options.Reliable | Options.Sequenced | Options.Ordered | Options.Res2)]
    [TestCase((byte)0, Options.Reliable | Options.Sequenced | Options.Ordered, Options.Reliable | Options.Res2)]
    public void Options_Are_NotValid(Options required, Options unallowed, Options options)
    {
        bool result = CheckMask(required, unallowed, options);
        Assert.That(result, Is.False);
    }

    private bool CheckMask(Options required, Options unallowed, Options options)
    {
        Options requiredMask = ~required;
        Options mask = requiredMask ^ unallowed;

        Options maskedOptions = mask | ~options;
        return maskedOptions == requiredMask;
    }
}
