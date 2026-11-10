using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class TerminalPaneInputTests
    {
        [Test]
        public void WheelBelongsToHoveredVisiblePaneWhileHostHasInput()
        {
            Assert.That(TerminalPaneInput.Wheel(true, true, EventType.ScrollWheel, true), Is.True);
            Assert.That(
                TerminalPaneInput.Wheel(true, true, EventType.ScrollWheel, false),
                Is.False
            );
            Assert.That(
                TerminalPaneInput.Wheel(false, true, EventType.ScrollWheel, true),
                Is.False
            );
            Assert.That(
                TerminalPaneInput.Wheel(true, false, EventType.ScrollWheel, true),
                Is.False
            );
            foreach (var type in new[] { EventType.KeyDown, EventType.MouseDown, EventType.Used })
                Assert.That(TerminalPaneInput.Wheel(true, true, type, true), Is.False);
        }

        [Test]
        public void HistoryAndRestoredAnchorDeferReflowUntilReturningLive()
        {
            Assert.That(TerminalViewportResize.Deferred(100, false, 0), Is.True);
            Assert.That(TerminalViewportResize.Deferred(0, true, 100), Is.True);
            Assert.That(TerminalViewportResize.Deferred(0, true, 0), Is.False);
            Assert.That(TerminalViewportResize.Deferred(0, false, 100), Is.False);
            Assert.That(TerminalViewportResize.Deferred(0, false, 0), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SplitAndDividerRetainHistoryGridWhileNewPaneAndReturnToLiveUseTheirBounds(
            bool restored
        )
        {
            var previous = SessionHub.Instance;
            try
            {
                var hub = SessionHub.Instance = new SessionHub();
                var live = new ScreenBuf
                {
                    Cols = 100,
                    Rows = 25,
                    History = 500,
                };
                hub.SizingScreens["original"] = live;
                var original = new TerminalPanel("original")
                {
                    Bounds = new UiLayoutRect(0, 0, 800, 500),
                };
                original.RefreshSize();
                hub.SizingMessages.Clear();
                original.HistoryPosition(50, restored);
                original.Bounds = new UiLayoutRect(0, 0, 400, 500);
                original.RefreshSize();
                original.DrawSize(live);
                Assert.That(
                    hub.SizingMessages,
                    Is.Empty,
                    "splitting cannot reflow the history being read"
                );
                Assert.That(original.SizingState.Cols, Is.EqualTo(100));
                Assert.That(original.HistoryOffset, Is.EqualTo(restored ? 0 : 50));

                var second = new TerminalPanel("second")
                {
                    Bounds = new UiLayoutRect(400, 0, 400, 500),
                };
                second.RefreshSize();
                Assert.That(hub.SizingMessages.Count, Is.EqualTo(1));
                Assert.That(hub.SizingMessages[0].Resize.Name, Is.EqualTo("second"));
                Assert.That(hub.SizingMessages[0].Resize.Cols, Is.EqualTo(50));
                hub.SizingMessages.Clear();

                original.Bounds = new UiLayoutRect(0, 0, 600, 500);
                original.DrawSize(live);
                Assert.That(original.SizingState.SizeDirty, Is.False);
                original.HistoryPosition(0);
                original.DrawSize(live);
                Assert.That(original.SizingState.SizeDirty, Is.True);
                Assert.That(
                    original.SizingState.Cols,
                    Is.EqualTo(75),
                    "return live uses latest divider bounds"
                );
                original.RefreshSize();
                Assert.That(hub.SizingMessages[0].Resize.Cols, Is.EqualTo(75));
            }
            finally
            {
                SessionHub.Instance = previous;
            }
        }

        [Test]
        public void EnteringHistoryCancelsPendingResizeAndRestoresTheLiveGridDimensions()
        {
            var previous = SessionHub.Instance;
            try
            {
                var hub = SessionHub.Instance = new SessionHub();
                var live = new ScreenBuf { Cols = 100, Rows = 25 };
                hub.SizingScreens["original"] = live;
                var pane = new TerminalPanel("original")
                {
                    Bounds = new UiLayoutRect(0, 0, 400, 500),
                };
                pane.DrawSize(live);
                Assert.That(pane.SizingState.SizeDirty, Is.True);
                pane.HistoryPosition(25);
                pane.DrawSize(live);
                Assert.That(pane.SizingState.SizeDirty, Is.False);
                Assert.That(pane.SizingState.Cols, Is.EqualTo(100));
                Assert.That(hub.SizingMessages, Is.Empty);
            }
            finally
            {
                SessionHub.Instance = previous;
            }
        }
    }
}
