using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public sealed class NativeScrollCoverageTests
    {
        [Test]
        public void AccumulatedSampleCoversMultipleLegacyPacketsWithoutSteppedJumps()
        {
            var coverage = new NativeScrollCoverage();
            float position = 0f;
            for (int frame = 10; frame < 30; frame += 2)
            {
                // Small native movement is applied once. Several legacy notches can
                // arrive over multiple IMGUI passes, including the following frame.
                position += 0.5f;
                coverage.Record(frame, 0.001f, 0.5f);
                for (int packet = 0; packet < 4; packet++)
                    if (!coverage.Covers(frame + packet / 2, 0f, 2f)) position += 40f;
            }
            Assert.That(position, Is.EqualTo(5f), "legacy packets must not add whole wheel steps");
        }

        [TestCase(10, 0f, -2f)]
        [TestCase(10, 2f, 0f)]
        [TestCase(10, 2f, 2f)]
        [TestCase(12, 0f, 2f)]
        public void ReversedUncoveredAndExpiredInputRetainsLogicalFallback(int frame, float dx, float dy)
        {
            var coverage = new NativeScrollCoverage();
            coverage.Record(10, 0f, 0.5f);
            Assert.That(coverage.Covers(frame, dx, dy), Is.False);
        }

        [Test]
        public void NewSampleReplacesDirectionAndDisableClearsCoverage()
        {
            var coverage = new NativeScrollCoverage();
            coverage.Record(10, 0f, 0.5f);
            coverage.Record(11, 0f, -0.5f);
            Assert.That(coverage.Covers(11, 0f, 2f), Is.False);
            Assert.That(coverage.Covers(11, 0f, -2f), Is.True);
            coverage.Clear();
            Assert.That(coverage.Covers(11, 0f, -2f), Is.False);
        }

        [Test]
        public void NoSampleCannotSuppressLogicalMovement()
        {
            var coverage = new NativeScrollCoverage();
            Assert.That(coverage.Covers(0, 0f, 2f), Is.False);
            coverage.Record(10, 0f, 0f);
            Assert.That(coverage.Covers(10, 0f, 2f), Is.False);
        }
    }
}
