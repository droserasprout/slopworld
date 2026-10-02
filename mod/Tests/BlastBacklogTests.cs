using System;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class BlastBacklogTests
    {
        [Test]
        public void CappedDrainRetainsOlderRingGeometryAcrossWaves()
        {
            var backlog = new BlastBacklog();
            backlog.AddRing(0, 100, 175);
            int firstCount = (int)Math.Floor(Math.PI * 100 * 100 / 175);
            for (int i = 0; i < 32; i++)
                Assert.That(backlog.TryTake(out _, out _), Is.True);
            backlog.AddRing(100, 200, 175);
            for (int i = 32; i < firstCount; i++)
            {
                Assert.That(backlog.TryTake(out var inner, out var outer), Is.True);
                Assert.That(inner, Is.EqualTo(0));
                Assert.That(outer, Is.EqualTo(100));
            }
            int total = firstCount;
            while (backlog.TryTake(out var inner, out var outer))
            {
                Assert.That(inner, Is.EqualTo(100));
                Assert.That(outer, Is.EqualTo(200));
                total++;
            }
            Assert.That(total, Is.EqualTo((int)Math.Floor(Math.PI * 200 * 200 / 175)));
            Assert.That(backlog.Pending, Is.False);
            Assert.That(backlog.TryTake(out _, out _), Is.False);
        }

        [Test]
        public void FractionalRingsAccumulateAndClearResetsAllWork()
        {
            var backlog = new BlastBacklog();
            for (int i = 0; i < 10; i++) backlog.AddRing(i, i + 1, 175);
            Assert.That(backlog.TryTake(out _, out _), Is.True);
            Assert.That(backlog.Pending, Is.False);
            backlog.Clear();
            backlog.Clear();
            backlog.AddRing(0, 7, 175);
            Assert.That(backlog.Pending, Is.False, "previous fraction must not carry into the next colony");
            backlog.AddRing(7, 20, 175);
            Assert.That(backlog.Pending, Is.True);
            backlog.Clear();
            Assert.That(backlog.TryTake(out _, out _), Is.False);
        }
    }
}
