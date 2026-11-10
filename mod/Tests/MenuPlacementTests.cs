using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class MenuPlacementTests
    {
        [Test]
        public void LeftOpeningChainContinuesAwayFromRoot()
        {
            var parent = new Rect(400, 100, 200, 300);
            var root = new Rect(599, 100, 200, 300);
            var placed = Place(parent, root);
            Assert.That(placed.x, Is.EqualTo(201f));
        }

        [Test]
        public void VerticallyClearRightSideRemainsPreferred()
        {
            var parent = new Rect(400, 100, 200, 300);
            var root = new Rect(599, 0, 200, 90);
            Assert.That(Place(parent, root).x, Is.EqualTo(599f));
        }

        [Test]
        public void ClearSideCanNarrowWhileBlockedSideHasFullWidth()
        {
            var parent = new Rect(150, 100, 200, 300);
            var root = new Rect(349, 100, 200, 300);
            var placed = Place(parent, root);
            Assert.That(placed.x, Is.EqualTo(1f));
            Assert.That(placed.width, Is.EqualTo(150f));
        }

        [Test]
        public void BothBlockedRetainScreenSpaceFallback()
        {
            var parent = new Rect(400, 100, 200, 300);
            var placed = MenuPlacement.Place(
                parent,
                new[] { parent, new Rect(200, 100, 200, 300), new Rect(599, 100, 200, 300) },
                new Vector2(200, 100),
                100,
                1000,
                800,
                100,
                1
            );
            Assert.That(placed.x, Is.EqualTo(599f));
        }

        static Rect Place(Rect parent, Rect root) =>
            MenuPlacement.Place(
                parent,
                new[] { parent, root },
                new Vector2(200, 100),
                100,
                1000,
                800,
                100,
                1
            );
    }
}
