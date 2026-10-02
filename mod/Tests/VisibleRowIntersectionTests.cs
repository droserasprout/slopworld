using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class VisibleRowIntersectionTests
    {
        [TestCase(0, 20, 20, 100, false)]
        [TestCase(120, 20, 20, 100, false)]
        [TestCase(1, 20, 20, 100, true)]
        [TestCase(119, 60, 20, 100, true)]
        [TestCase(20, 20, 20, 0, false)]
        [TestCase(20, 0, 20, 100, false)]
        public void ClipBoundaries(float y, float rowHeight, float top, float height, bool expected)
        {
            Assert.That(VisibleRows.Intersects(y, rowHeight, top, height), Is.EqualTo(expected));
        }

        [Test]
        public void IntersectingRowCountIsBoundedForBothSingleAndStackedRows()
        {
            foreach (float rowHeight in new[] { 24f, 72f })
            {
                int visible = 0;
                for (int row = 0; row < 10000; row++)
                    if (VisibleRows.Intersects(row * rowHeight, rowHeight, 12345f, 480f)) visible++;
                Assert.That(visible, Is.LessThanOrEqualTo((int)(480f / rowHeight) + 2));
                Assert.That(visible, Is.GreaterThan(0));
            }
        }
    }
}
