using System;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class ScrollWheelRouterTests
    {
        [SetUp]
        public void Reset()
        {
            SmoothScroll.WheelOnly = false;
            SmoothScroll.WheelTrace.Clear();
            GUIUtility.Origin = new Vector2();
        }

        [TearDown]
        public void Cleanup() => Reset();

        [Test]
        public void StormSkipsContentAndPreservesNestedEndOrder()
        {
            var router = new ScrollWheelRouter();
            var outer = new SmoothScroll();
            var inner = new SmoothScroll();
            var sibling = new SmoothScroll();
            var bounds = new Rect(10, 20, 300, 200);
            var view = new Rect(0, 0, 280, 1000);
            int draws = 0;
            Action draw = () =>
            {
                draws++;
                using (outer.Scope(bounds, view))
                using (inner.Scope(new Rect(0, 50, 100, 100), view)) { }
                using (sibling.Scope(new Rect(400, 0, 100, 100), view)) { }
            };
            router.Draw(bounds, draw);
            SmoothScroll.WheelTrace.Clear();
            SmoothScroll.WheelOnly = true;
            for (int i = 0; i < 1000; i++) router.Draw(bounds, draw);
            Assert.That(draws, Is.EqualTo(1));
            Assert.That(SmoothScroll.WheelTrace.Count, Is.EqualTo(3000));
            for (int i = 0; i < 3000; i += 3)
            {
                Assert.That(SmoothScroll.WheelTrace[i], Is.SameAs(inner));
                Assert.That(SmoothScroll.WheelTrace[i + 1], Is.SameAs(outer));
                Assert.That(SmoothScroll.WheelTrace[i + 2], Is.SameAs(sibling));
            }
            Assert.That(GUIUtility.Origin, Is.EqualTo(new Vector2()));
            router.Invalidate();
            router.Draw(bounds, draw);
            Assert.That(draws, Is.EqualTo(2));
            router.Draw(new Rect(0, 0, 100, 100), draw);
            Assert.That(draws, Is.EqualTo(3));
            SmoothScroll.WheelOnly = false;
            router.Draw(bounds, draw);
            Assert.That(draws, Is.EqualTo(4));
        }

        [Test]
        public void FirstWheelCannotPublishAnIncompleteTree()
        {
            var router = new ScrollWheelRouter();
            var scroll = new SmoothScroll();
            var bounds = new Rect(0, 0, 100, 100);
            int draws = 0;
            Action draw = () =>
            {
                draws++;
                if (SmoothScroll.WheelOnly) return;
                using (scroll.Scope(bounds, bounds)) { }
            };
            SmoothScroll.WheelOnly = true;
            router.Draw(bounds, draw);
            router.Draw(bounds, draw);
            Assert.That(draws, Is.EqualTo(2));
            SmoothScroll.WheelOnly = false;
            router.Draw(bounds, draw);
            SmoothScroll.WheelOnly = true;
            SmoothScroll.WheelOnly = true;
            router.Draw(bounds, draw);
            Assert.That(draws, Is.EqualTo(3));
        }

        [Test]
        public void UnknownTransformsAndFailedDrawsUseOrdinaryPath()
        {
            var router = new ScrollWheelRouter();
            var scroll = new SmoothScroll();
            var bounds = new Rect(0, 0, 100, 100);
            int draws = 0;
            Action draw = () =>
            {
                draws++;
                GUIUtility.Origin = new Vector2(20, 20);
                using (scroll.Scope(bounds, bounds)) { }
                GUIUtility.Origin = new Vector2();
            };
            router.Draw(bounds, draw);
            SmoothScroll.WheelOnly = true;
            router.Draw(bounds, draw);
            Assert.That(draws, Is.EqualTo(2));
            SmoothScroll.WheelOnly = false;
            router.Draw(bounds, () => { using (scroll.Scope(bounds, bounds)) { } });
            Assert.Throws<InvalidOperationException>(() => router.Draw(bounds, () =>
            {
                using (scroll.Scope(bounds, bounds)) throw new InvalidOperationException();
            }));
            SmoothScroll.WheelOnly = true;
            router.Draw(bounds, draw);
            Assert.That(draws, Is.EqualTo(3));
        }
    }
}
