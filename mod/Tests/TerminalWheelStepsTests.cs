using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class TerminalWheelStepsTests
    {
        [TestCase(1)]
        [TestCase(-1)]
        public void ReaderDistanceIsPreservedWhenNotchesArriveInFewerFrames(int direction)
        {
            // Twelve physical notches, delivered individually, in pairs, or in one frame.
            foreach (int perFrame in new[] { 1, 2, 3, 6, 12 })
            {
                int total = 0;
                for (int frame = 0; frame < 12 / perFrame; frame++)
                    total += TerminalWheelSteps.Count(direction * perFrame * 3f, true);
                Assert.That(total, Is.EqualTo(12), "notches per frame: " + perFrame);
            }
        }

        [TestCase(0.1f)]
        [TestCase(-0.1f)]
        [TestCase(3f)]
        [TestCase(-3f)]
        public void FineGesturesAndSingleNotchesKeepOneReaderStep(float delta) =>
            Assert.That(TerminalWheelSteps.Count(delta, true), Is.EqualTo(1));

        [Test]
        public void LargeReaderBatchFitsTheMouseProtocolCount() =>
            Assert.That(TerminalWheelSteps.Count(3000f, true), Is.EqualTo(byte.MaxValue));

        [TestCase(0.1f, 1)]
        [TestCase(-3f, 3)]
        [TestCase(30f, 5)]
        public void OtherApplicationsRetainTheirExistingWheelSpeed(float delta, int expected) =>
            Assert.That(TerminalWheelSteps.Count(delta, false), Is.EqualTo(expected));
    }
}
