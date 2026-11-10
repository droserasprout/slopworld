using System;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class SmoothScrollInteractionTests
    {
        static readonly Rect Bounds = new Rect(20, 30, 100, 100);
        static readonly Rect View = new Rect(0, 0, 100, 1000);
        Event _event;
        Vector2 _origin;
        Vector2 _raw;
        Vector2 _sample;
        bool _smooth;
        bool _blocked;
        bool _available;
        int _hot;
        int _discards;

        [SetUp]
        public void SetUp()
        {
            _event = Event.current;
            _origin = GUIUtility.Origin;
            _hot = GUIUtility.hotControl;
            _raw = Input.mouseScrollDelta;
            _sample = X11ScrollInput.Sample;
            _available = X11ScrollInput.Available;
            _discards = X11ScrollInput.Discards;
            _smooth = Settings.S.smoothScrolling;
            _blocked = UiMenu.ScrollBlocked;
            ClearInput();
        }

        static void ClearInput()
        {
            Time.frameCount++;
            Settings.S.smoothScrolling = false;
            UiMenu.ScrollBlocked = false;
            GUIUtility.Origin = Vector2.zero;
            GUIUtility.hotControl = 0;
            Input.mouseScrollDelta = Vector2.zero;
            X11ScrollInput.Sample = Vector2.zero;
            X11ScrollInput.Available = false;
            Event.current = new Event { type = EventType.Repaint };
            var scroll = new SmoothScroll();
            scroll.BeginInput(Bounds, Vector2.zero);
            scroll.EndInput();
        }

        [TearDown]
        public void TearDown()
        {
            ClearInput();
            Event.current = _event;
            GUIUtility.Origin = _origin;
            GUIUtility.hotControl = _hot;
            Input.mouseScrollDelta = _raw;
            X11ScrollInput.Sample = _sample;
            X11ScrollInput.Available = _available;
            X11ScrollInput.Discards = _discards;
            Settings.S.smoothScrolling = _smooth;
            UiMenu.ScrollBlocked = _blocked;
        }

        static Event Wheel(float delta = 1f) =>
            new Event
            {
                type = EventType.ScrollWheel,
                mousePosition = new Vector2(40, 50),
                delta = new Vector2(0, delta),
            };

        static void Draw(SmoothScroll scroll, Event e, bool bars = true)
        {
            Event.current = e;
            using (scroll.Scope(Bounds, View, bars, false)) { }
        }

        static Event Mouse(EventType type, float x, float y, int button = 0) =>
            new Event
            {
                type = type,
                mousePosition = new Vector2(x, y),
                button = button,
            };

        [Test]
        public void RevealMovesOnlyEnoughToExposeTheRowAndPreservesHorizontalPosition()
        {
            var scroll = new SmoothScroll();
            scroll.JumpTo(new Vector2(17, 100));
            scroll.Reveal(100, 100, 100);
            Assert.That(scroll.Position, Is.EqualTo(new Vector2(17, 100)));
            scroll.Reveal(120, 20, 100);
            Assert.That(scroll.Position, Is.EqualTo(new Vector2(17, 100)));
            scroll.Reveal(80, 10, 100);
            Assert.That(scroll.Position, Is.EqualTo(new Vector2(17, 80)));
            scroll.Reveal(190, 20, 100);
            Assert.That(scroll.Position, Is.EqualTo(new Vector2(17, 110)));
        }

        [Test]
        public void RevealOversizedRowPrioritizesItsTopWhenItStartsAboveTheViewport()
        {
            var scroll = new SmoothScroll();
            scroll.JumpTo(new Vector2(7, 100));
            scroll.Reveal(50, 200, 100);
            Assert.That(scroll.Position, Is.EqualTo(new Vector2(7, 50)));
        }

        [Test]
        public void CachedWheelExtentRequiresMeasurementAndMatchingViewport()
        {
            var scroll = new SmoothScroll();
            Event.current = Wheel();
            Assert.That(scroll.HandleWheel(Bounds), Is.False);
            Assert.That(Event.current.type, Is.EqualTo(EventType.ScrollWheel));

            Draw(scroll, new Event { type = EventType.Repaint }, false);
            Event.current = Wheel();
            var resized = new Rect(Bounds.x, Bounds.y, Bounds.width, Bounds.height + 1);
            Assert.That(scroll.HandleWheel(resized), Is.False);
            Assert.That(Event.current.type, Is.EqualTo(EventType.ScrollWheel));
            Assert.That(scroll.Position.y, Is.Zero);

            Assert.That(scroll.HandleWheel(Bounds), Is.True);
            Assert.That(scroll.Position.y, Is.EqualTo(20f));
            Assert.That(Event.current.type, Is.EqualTo(EventType.Used));
            Assert.That(SmoothScroll.WheelOnly, Is.True);
            Assert.That(scroll.HandleWheel(Bounds), Is.True);
            Assert.That(scroll.Position.y, Is.EqualTo(20f), "a consumed packet cannot move twice");
        }

        [Test]
        public void ExplicitWheelExtentRejectsUnknownHeightAndNonWheelPasses()
        {
            var scroll = new SmoothScroll();
            scroll.JumpTo(new Vector2(5, 300));
            Event.current = Wheel();
            Assert.That(scroll.HandleWheel(Bounds, -1), Is.False);
            Assert.That(Event.current.type, Is.EqualTo(EventType.ScrollWheel));
            Event.current = new Event { type = EventType.Repaint };
            Assert.That(scroll.HandleWheel(Bounds, 1000), Is.False);
            Assert.That(scroll.Position, Is.EqualTo(new Vector2(5, 300)));
        }

        [Test]
        public void FittingContentAndOutsidePointerLeaveWheelAvailableToOtherOwners()
        {
            var scroll = new SmoothScroll();
            scroll.JumpTo(new Vector2(5, 300));
            Event.current = Wheel();
            Assert.That(scroll.HandleWheel(Bounds, Bounds.height), Is.True);
            Assert.That(scroll.Position, Is.EqualTo(Vector2.zero));
            Assert.That(Event.current.type, Is.EqualTo(EventType.ScrollWheel));

            Event.current = Wheel();
            Event.current.mousePosition = new Vector2(-10, -10);
            Assert.That(scroll.HandleWheel(Bounds, 1000), Is.True);
            Assert.That(scroll.Position, Is.EqualTo(Vector2.zero));
            Assert.That(Event.current.type, Is.EqualTo(EventType.ScrollWheel));
        }

        [Test]
        public void ShrinkingExtentsClampBothAxesBeforeInput()
        {
            var scroll = new SmoothScroll();
            scroll.JumpTo(new Vector2(500, -20));
            scroll.BeginInput(Bounds, new Vector2(40, 70));
            scroll.EndInput();
            Assert.That(scroll.Position, Is.EqualTo(new Vector2(40, 0)));
            scroll.JumpTo(new Vector2(30, 90));
            scroll.BeginInput(Bounds, new Vector2(10, 20));
            scroll.EndInput();
            Assert.That(scroll.Position, Is.EqualTo(new Vector2(10, 20)));
        }

        [Test]
        public void ThumbGrabPreservesOffsetAndDragClampsUntilLeftButtonRelease()
        {
            var scroll = new SmoothScroll();
            // The harness thumb is the whole track; start at its top to test drag ownership.
            scroll.JumpTo(Vector2.zero);
            var track = UiScrollbar.Track(Bounds);
            float height = UiScrollbar.ThumbHeight(track, Bounds.height, 900);
            var thumb = UiScrollbar.Thumb(track, height, 0f);
            float x = thumb.x + thumb.width / 2;
            float y = thumb.y + height / 3;

            Draw(scroll, Mouse(EventType.MouseDown, x, y));
            int captured = GUIUtility.hotControl;
            Assert.That(captured, Is.Not.Zero);
            Assert.That(Event.current.type, Is.EqualTo(EventType.Used));
            Assert.That(scroll.Position.y, Is.EqualTo(0f).Within(.001f));

            Draw(scroll, Mouse(EventType.MouseDrag, x, y + 20));
            Assert.That(
                scroll.Position.y,
                Is.EqualTo(20f * 900f / (track.height - height)).Within(.001f)
            );
            Assert.That(Event.current.type, Is.EqualTo(EventType.Used));
            Draw(scroll, Mouse(EventType.MouseDrag, -1000, -1000));
            Assert.That(scroll.Position.y, Is.Zero);
            Draw(scroll, Mouse(EventType.MouseDrag, -1000, 1000));
            Assert.That(scroll.Position.y, Is.EqualTo(900f));

            Draw(scroll, Mouse(EventType.MouseUp, x, y, 1));
            Assert.That(GUIUtility.hotControl, Is.EqualTo(captured));
            Assert.That(Event.current.type, Is.EqualTo(EventType.MouseUp));
            Draw(scroll, Mouse(EventType.MouseUp, -1000, -1000));
            Assert.That(GUIUtility.hotControl, Is.Zero);
            Assert.That(Event.current.type, Is.EqualTo(EventType.Used));
            Draw(scroll, Mouse(EventType.MouseDrag, x, y));
            Assert.That(scroll.Position.y, Is.EqualTo(900f));
            Assert.That(Event.current.type, Is.EqualTo(EventType.MouseDrag));
        }

        [Test]
        public void ScrollbarPressRespectsAnotherControlsCaptureAndRequiresLeftButton()
        {
            var scroll = new SmoothScroll();
            var track = UiScrollbar.Track(Bounds);
            float x = track.x + track.width / 2;
            float y = track.y + track.height / 2;
            GUIUtility.hotControl = 999;
            Draw(scroll, Mouse(EventType.MouseDown, x, y));
            Assert.That(GUIUtility.hotControl, Is.EqualTo(999));
            Assert.That(scroll.Position.y, Is.Zero);
            Assert.That(Event.current.type, Is.EqualTo(EventType.MouseDown));

            GUIUtility.hotControl = 0;
            Draw(scroll, Mouse(EventType.MouseDown, x, y, 1));
            Assert.That(GUIUtility.hotControl, Is.Zero);
            Draw(scroll, Mouse(EventType.MouseDown, x, y));
            Assert.That(scroll.Position.y, Is.EqualTo(0f).Within(.001f));
            Assert.That(GUIUtility.hotControl, Is.Not.Zero);
            Draw(scroll, Mouse(EventType.MouseUp, x, y));
            Assert.That(GUIUtility.hotControl, Is.Zero);
        }

        [Test]
        public void RepeatedInnerScopeDisposalPreservesEnclosingGroups()
        {
            var outer = new SmoothScroll();
            var inner = new SmoothScroll();
            var pointer = new Vector2(60, 80);
            Event.current.mousePosition = pointer;
            GUIUtility.Origin = new Vector2(7, 9);
            var origin = GUIUtility.Origin;
            using (outer.Scope(Bounds, View, false, false))
            {
                var enclosingOrigin = GUIUtility.Origin;
                var enclosingPointer = Event.current.mousePosition;
                var scope = inner.Scope(new Rect(3, 4, 50, 50), View, false, false);
                scope.Dispose();
                scope.Dispose();
                Assert.That(GUIUtility.Origin, Is.EqualTo(enclosingOrigin));
                Assert.That(Event.current.mousePosition, Is.EqualTo(enclosingPointer));
            }
            Assert.That(GUIUtility.Origin, Is.EqualTo(origin));
            Assert.That(Event.current.mousePosition, Is.EqualTo(pointer));
        }

        [Test]
        public void ScopeRestoresGroupsWhenContentThrowsAndRemainsSafeToDisposeAgain()
        {
            var scroll = new SmoothScroll();
            scroll.JumpTo(new Vector2(0, 200));
            var pointer = new Vector2(60, 80);
            Event.current.mousePosition = pointer;
            var origin = GUIUtility.Origin;
            IDisposable scope = null;
            Assert.Throws<InvalidOperationException>(() =>
            {
                using (scope = scroll.Scope(Bounds, View, false, false))
                    throw new InvalidOperationException("row failure");
            });
            scope.Dispose();
            Assert.That(GUIUtility.Origin, Is.EqualTo(origin));
            Assert.That(Event.current.mousePosition, Is.EqualTo(pointer));
            Draw(scroll, new Event { type = EventType.Repaint }, false);
            Assert.That(GUIUtility.Origin, Is.EqualTo(origin));
        }
    }
}
