using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class SmoothScrollTests
    {
        static readonly Rect Bounds = new Rect(0, 0, 100, 100);
        static readonly Vector2 Max = new Vector2(10000, 10000);

        [SetUp]
        public void Reset()
        {
            Time.frameCount++;
            Settings.S.smoothScrolling = false;
            UiMenu.ScrollBlocked = false;
            Input.mouseScrollDelta = Vector2.zero;
            GUIUtility.Origin = Vector2.zero;
            Event.current = new Event { type = EventType.Repaint };
            var clear = new SmoothScroll();
            clear.BeginInput(Bounds, Max);
            clear.EndInput();
            Settings.S.smoothScrolling = true;
            X11ScrollInput.Sample = Vector2.zero;
            X11ScrollInput.Available = false;
            X11ScrollInput.Discards = 0;
        }

        [TearDown]
        public void Cleanup() => Reset();

        static Event Wheel(float delta, float pointer = 10f) =>
            new Event
            {
                type = EventType.ScrollWheel,
                delta = new Vector2(0, delta),
                mousePosition = new Vector2(pointer, pointer),
            };

        static void Apply(SmoothScroll scroll)
        {
            scroll.BeginInput(Bounds, Max);
            scroll.EndInput();
        }

        [Test]
        public void NestedOwnerGetsMotionOnceAndOuterKeepsItsPosition()
        {
            var outer = new SmoothScroll();
            var inner = new SmoothScroll();
            var router = new ScrollWheelRouter();
            int rows = 0;
            System.Action draw = () =>
            {
                rows++;
                using (outer.Scope(Bounds, new Rect(0, 0, 100, 1000), false))
                using (inner.Scope(Bounds, new Rect(0, 0, 100, 1000), false)) { }
            };
            router.Draw(Bounds, draw);
            Event.current = Wheel(.5f);
            X11ScrollInput.Sample = new Vector2(0, .125f);
            X11ScrollInput.Available = true;
            router.Draw(Bounds, draw);
            Assert.That(rows, Is.EqualTo(1));
            Assert.That(outer.Position.y, Is.Zero);
            Assert.That(inner.Position.y, Is.EqualTo(5f).Within(.001f));
            Assert.That(GUIUtility.Origin, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void OrdinaryFallbackRetainsRawFractionAndNonWheelNativeInputStillMoves()
        {
            var scroll = new SmoothScroll();
            var sources = new List<string>();
            scroll.ObserveInput = (_, _, source) => sources.Add(source);
            Event.current = Wheel(1f);
            Input.mouseScrollDelta = new Vector2(0, .125f);
            Apply(scroll);
            Assert.That(scroll.Position.y, Is.EqualTo(2.5f));
            Time.frameCount++;
            Event.current = new Event
            {
                type = EventType.Repaint,
                mousePosition = new Vector2(10, 10),
            };
            X11ScrollInput.Sample = new Vector2(0, .125f);
            X11ScrollInput.Available = true;
            Apply(scroll);
            Assert.That(scroll.Position.y, Is.EqualTo(7.5f));
            Assert.That(sources, Does.Contain("precise"));
        }

        [Test]
        public void CoveredNativeMovementCannotSuppressADifferentPointerOwner()
        {
            var left = new SmoothScroll();
            var right = new SmoothScroll();
            Event.current = Wheel(1f);
            X11ScrollInput.Sample = new Vector2(0, .25f);
            X11ScrollInput.Available = true;
            Apply(left);
            Event.current = Wheel(1f, pointer: 20f);
            Apply(right);
            Assert.That(left.Position.y, Is.EqualTo(10f));
            Assert.That(right.Position.y, Is.EqualTo(20f));
        }

        [Test]
        public void MenuBlocksNativeAndLogicalClaimsAndDisabledSmoothingUsesLogicalSum()
        {
            var scroll = new SmoothScroll();
            UiMenu.ScrollBlocked = true;
            Event.current = Wheel(1f);
            X11ScrollInput.Sample = new Vector2(0, .25f);
            X11ScrollInput.Available = true;
            Apply(scroll);
            Assert.That(scroll.Position.y, Is.Zero);
            Assert.That(Event.current.type, Is.EqualTo(EventType.ScrollWheel));
            UiMenu.ScrollBlocked = false;
            Settings.S.smoothScrolling = false;
            for (int i = 0; i < 12; i++)
            {
                Event.current = Wheel(.25f);
                Apply(scroll);
            }
            Assert.That(scroll.Position.y, Is.EqualTo(60f));
        }
    }
}
