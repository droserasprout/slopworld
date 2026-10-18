using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class TerminalSelectionStateTests
    {
        public static void StreamingKeepsFrozenHistorySelectionOnItsText()
        {
            var live = Frame(1, 10, 0, "live-a", "live-b", "live-c");
            var history = new TerminalHistory();
            history.Reset(live);
            history.Add(Frame(1, 10, 3, "older", "selected", "newer"), live);
            Assert.That(history.TryView(3, false, out var displayed, freeze: true), Is.True);
            var selection = new TerminalSelectionState();
            selection.NoteLiveFrame(live.Seq, 0);
            selection.SyncOffset(displayed.Off);
            selection.HasSelection = true;
            selection.A = new Vector2Int(0, 1);
            selection.B = new Vector2Int(7, 1);

            for (ulong seq = 2; seq <= 4; seq++)
            {
                var next = Frame(seq, 10 + (int)seq - 1, 0, "output", "more", "tail");
                selection.NoteLiveFrame(next.Seq, 1);
                history.UpdateLive(next, 1);
                Assert.That(
                    history.TryView(3 + (int)seq - 1, false, out displayed, freeze: true),
                    Is.True
                );
                selection.SyncOffset(displayed.Off);
                Assert.That(selection.A, Is.EqualTo(new Vector2Int(0, 1)));
                Assert.That(selection.B, Is.EqualTo(new Vector2Int(7, 1)));
                Assert.That(
                    history.SelectionText(
                        displayed,
                        selection.A.x,
                        selection.A.y,
                        selection.B.x,
                        selection.B.y
                    ),
                    Is.EqualTo("selected")
                );
            }
        }

        public static void StreamingPreservesWordAndLineDragAnchorsWhileUserScrolls()
        {
            var selection = new TerminalSelectionState();
            selection.NoteLiveFrame(1, 0);
            selection.SyncOffset(10);
            selection.Control = 1;
            selection.Mouse = new Vector2(20, 30);
            selection.SelectionMoved = true;
            selection.MultiClickSelection = true;
            selection.EdgeDirection = -1;
            selection.Dragging = true;
            selection.WordDragging = true;
            selection.LineDragging = true;
            selection.A = new Vector2Int(2, 1);
            selection.B = new Vector2Int(5, 3);
            selection.WordStart = new Vector2Int(1, 1);
            selection.WordEnd = new Vector2Int(6, 1);
            selection.LineStart = 1;

            selection.NoteLiveFrame(2, 3);
            selection.NoteLiveFrame(2, 3);
            selection.SyncOffset(13);
            Assert.That(selection.A, Is.EqualTo(new Vector2Int(2, 1)));
            Assert.That(selection.B, Is.EqualTo(new Vector2Int(5, 3)));
            Assert.That(selection.WordStart, Is.EqualTo(new Vector2Int(1, 1)));
            Assert.That(selection.WordEnd, Is.EqualTo(new Vector2Int(6, 1)));
            Assert.That(selection.LineStart, Is.EqualTo(1));

            selection.SyncOffset(15);
            Assert.That(selection.A.y, Is.EqualTo(3));
            Assert.That(selection.B.y, Is.EqualTo(5));
            Assert.That(selection.WordStart.y, Is.EqualTo(3));
            Assert.That(selection.WordEnd.y, Is.EqualTo(3));
            Assert.That(selection.LineStart, Is.EqualTo(3));
            Assert.That(selection.Control, Is.EqualTo(1));
            Assert.That(selection.Mouse, Is.EqualTo(new Vector2(20, 30)));
            Assert.That(selection.SelectionMoved, Is.True);
            Assert.That(selection.MultiClickSelection, Is.True);
            Assert.That(selection.EdgeDirection, Is.EqualTo(-1));
        }

        public static void LiveSelectionFollowsOutputAndResetsSequenceForNewStreams()
        {
            var selection = new TerminalSelectionState
            {
                HasSelection = true,
                A = new Vector2Int(1, 2),
                B = new Vector2Int(4, 2),
            };
            selection.NoteLiveFrame(1, 5);
            Assert.That(selection.A.y, Is.EqualTo(2));
            selection.NoteLiveFrame(2, 1);
            selection.SyncOffset(0);
            Assert.That(selection.A.y, Is.EqualTo(1));
            Assert.That(selection.B.y, Is.EqualTo(1));
            selection.NoteLiveFrame(3, 0);
            Assert.That(selection.A.y, Is.EqualTo(1));

            selection.ResetLiveSequence();
            selection.NoteLiveFrame(1, 5);
            Assert.That(selection.A.y, Is.EqualTo(1));
            selection.SyncOffset(10);
            selection.ResetCoordinates();
            Assert.That(selection.Offset, Is.Zero);
            Assert.That(selection.LastLiveSeq, Is.Null);
        }

        static ScreenBuf Frame(ulong seq, int history, int offset, params string[] lines) =>
            new ScreenBuf
            {
                Seq = seq,
                History = history,
                Off = offset,
                Cols = 20,
                Rows = 3,
                Lines = lines,
            };
    }
}
