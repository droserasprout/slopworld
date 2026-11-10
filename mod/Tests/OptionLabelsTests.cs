using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class OptionLabelsTests
    {
        public static void AutoShowsDetectedOptionOnlyWhenKnown()
        {
            Assert.That(OptionLabels.Auto(), Is.EqualTo("Auto"));
            Assert.That(OptionLabels.Auto(""), Is.EqualTo("Auto"));
            Assert.That(OptionLabels.Auto(" "), Is.EqualTo("Auto"));
            Assert.That(OptionLabels.Auto("PipeWire"), Is.EqualTo("Auto (PipeWire)"));
        }
    }
}
