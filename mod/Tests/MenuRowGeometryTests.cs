using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class MenuRowGeometryTests
    {
        [Test]
        public void MixedRowsUseHalfOpenBoundsForHitAndDrawing()
        {
            var rows = new MenuRowGeometry();
            rows.Build(4, i => i == 1 ? 8f : 24f);

            Assert.That(rows.Height, Is.EqualTo(80f));
            Assert.That(rows.At(-1f), Is.EqualTo(-1));
            Assert.That(rows.At(24f), Is.EqualTo(1));
            Assert.That(rows.At(32f), Is.EqualTo(2));
            Assert.That(rows.At(80f), Is.EqualTo(-1));

            rows.Visible(25f, 31f, out int first, out int end);
            Assert.That((first, end), Is.EqualTo((1, 3)));
            rows.Visible(32f, 24f, out first, out end);
            Assert.That((first, end), Is.EqualTo((2, 3)));
            rows.Visible(80f, 20f, out first, out end);
            Assert.That((first, end), Is.EqualTo((4, 4)));
        }

        [Test]
        public void RebuildReplacesOldExtentAndIndexesDeepList()
        {
            var rows = new MenuRowGeometry();
            rows.Build(100000, i => i % 10 == 0 ? 8f : 24f);
            float nearEnd = rows.Top(99990);
            rows.Visible(nearEnd, 40f, out int first, out int end);
            Assert.That((first, end), Is.EqualTo((99990, 99993)));

            rows.Build(1, _ => 12f);
            Assert.That(rows.Height, Is.EqualTo(12f));
            Assert.That(rows.At(12f), Is.EqualTo(-1));
            rows.Visible(0f, 20f, out first, out end);
            Assert.That((first, end), Is.EqualTo((0, 1)));
        }
    }
}
