using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public sealed class DocumentSelectionGestureTests
    {
        // Keep gestures independent of OS clipboard ownership; adapters' clipboard
        // contracts are exercised by DocumentSelectionTests.
        sealed class Selection : DocumentSelection<DocumentSelectionLine>
        {
            protected override bool PublishPrimary => false;
            protected override bool CopyOnSelectAll => false;

            protected override void EnsureEdges(DocumentSelectionLine line) { }

            protected override string CopyRange(Vector2Int start, Vector2Int end) =>
                string.Join(
                    "\n",
                    Lines
                        .Skip(start.y)
                        .Take(end.y - start.y + 1)
                        .Select(
                            (line, i) =>
                            {
                                int first = i == 0 ? start.x : 0;
                                int last = start.y + i == end.y ? end.x : line.Text.Length;
                                return line.Text.Substring(first, last - first);
                            }
                        )
                );

            public Selection(params string[] texts)
            {
                for (int i = 0; i < texts.Length; i++)
                {
                    var line = new DocumentSelectionLine
                    {
                        Text = texts[i],
                        LogicalIndex = i,
                        Y = i * 20,
                        Height = 10,
                        Width = texts[i].Length,
                    };
                    for (int x = 0; x <= texts[i].Length; x++)
                        line.Edges.Add(x);
                    Lines.Add(line);
                }
                Reindex();
            }

            public void Reorder() => Reindex();
        }

        static readonly Rect Body = new Rect(100, 50, 200, 200);

        static Event Mouse(float x, float y, bool shift = false) =>
            new Event
            {
                type = EventType.MouseDown,
                mousePosition = new Vector2(Body.x + x, Body.y + y),
                shift = shift,
            };

        int _hotControl;

        [SetUp]
        public void SetUp()
        {
            _hotControl = GUIUtility.hotControl;
            GUIUtility.hotControl = 0;
            EditorTrace.Draws.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            GUIUtility.hotControl = _hotControl;
            EditorTrace.Draws.Clear();
        }

        [Test]
        public void WordDragExtendsBothDirectionsAndIgnoresEmptyLines()
        {
            var selection = new Selection("first word", "", "last word");
            selection.BeginMouse(Body, Mouse(7, 45), 2, new Vector2());
            Assert.That(selection.SelectionText(), Is.EqualTo("word"));
            selection.DragMouse(Body, Mouse(1, 5), new Vector2());
            Assert.That(selection.SelectionText(), Is.EqualTo("first word\n\nlast word"));
            selection.DragMouse(Body, Mouse(1, 25), new Vector2());
            Assert.That(selection.SelectionText(), Is.EqualTo("first word\n\nlast word"));
            selection.EndMouse(Body, Mouse(1, 45), new Vector2());
            Assert.That(selection.SelectionText(), Is.EqualTo("last word"));
            Assert.That(GUIUtility.hotControl, Is.Zero);
            var idle = Mouse(1, 5);
            selection.DragMouse(Body, idle, new Vector2());
            selection.EndMouse(Body, idle, new Vector2());
            Assert.That(idle.type, Is.EqualTo(EventType.MouseDown));
            Assert.That(selection.SelectionText(), Is.EqualTo("last word"));
        }

        [Test]
        public void TripleClickDragIncludesWholeLinesInEitherDirection()
        {
            var selection = new Selection("first", "middle", "last");
            selection.BeginMouse(Body, Mouse(2, 25), 3, new Vector2());
            Assert.That(selection.SelectionText(), Is.EqualTo("middle"));
            selection.DragMouse(Body, Mouse(2, 5), new Vector2());
            Assert.That(selection.SelectionText(), Is.EqualTo("first\nmiddle"));
            selection.EndMouse(Body, Mouse(2, 45), new Vector2());
            Assert.That(selection.SelectionText(), Is.EqualTo("middle\nlast"));
            Assert.That(GUIUtility.hotControl, Is.Zero);
        }

        [TestCase(2)]
        [TestCase(3)]
        public void MultiClickOnEmptyLineClearsSelectionAndCapture(int clicks)
        {
            var selection = new Selection("text", "");
            selection.SelectAll();
            selection.BeginMouse(Body, Mouse(0, 25), clicks, new Vector2());
            Assert.That(selection.HasSelection, Is.False);
            Assert.That(selection.SelectionText(), Is.Empty);
            Assert.That(GUIUtility.hotControl, Is.Zero);
        }

        [Test]
        public void ShiftClickKeepsAnchorOnlyWhenThereIsAnExistingSelection()
        {
            var selection = new Selection("abcdef");
            selection.BeginMouse(Body, Mouse(2, 5, true), 1, new Vector2());
            Assert.That(selection.HasSelection, Is.False);
            selection.EndMouse(Body, Mouse(4, 5), new Vector2());
            Assert.That(selection.SelectionText(), Is.EqualTo("cd"));
            selection.BeginMouse(Body, Mouse(0, 5, true), 1, new Vector2());
            Assert.That(selection.SelectionText(), Is.EqualTo("ab"));
            selection.EndMouse(Body, Mouse(1, 5), new Vector2());
            Assert.That(selection.SelectionText(), Is.EqualTo("b"));
            selection.BeginMouse(Body, Mouse(4, 5), 1, new Vector2());
            Assert.That(selection.HasSelection, Is.False);
            selection.Clear();
            Assert.That(GUIUtility.hotControl, Is.Zero);
        }

        [Test]
        public void HitTestingUsesScrolledSpatialOrderButCopyUsesLogicalOrder()
        {
            var selection = new Selection("left", "right", "below");
            selection.Lines[0].X = 20;
            selection.Lines[1].Y = 0;
            selection.Lines[2].Y = 20;
            selection.Reorder();
            // The right-hand cell precedes the left-hand cell in logical copy order.
            selection.BeginMouse(Body, Mouse(1, 5), 1, new Vector2(20, 0));
            selection.EndMouse(Body, Mouse(3, 5), new Vector2(0, 20));
            Assert.That(selection.SelectionText(), Is.EqualTo("eft\nright\nbel"));
            selection.BeginMouse(Body, Mouse(1, 5), 1, new Vector2());
            selection.EndMouse(Body, Mouse(3, 5), new Vector2());
            Assert.That(selection.SelectionText(), Is.EqualTo("ig"));
        }

        [Test]
        public void OutsidePressDoesNotCaptureAndEmptyDocumentStaysEmpty()
        {
            var selection = new Selection();
            var outside = Mouse(-1, 0);
            selection.BeginMouse(Body, outside, 1, new Vector2());
            Assert.That(outside.type, Is.EqualTo(EventType.MouseDown));
            Assert.That(GUIUtility.hotControl, Is.Zero);
            selection.SelectAll();
            selection.BeginMouse(Body, Mouse(1, 5), 1, new Vector2());
            selection.EndMouse(Body, Mouse(200, 200), new Vector2());
            Assert.That(selection.HasSelection, Is.False);
            Assert.That(selection.SelectionText(), Is.Empty);
            Assert.That(GUIUtility.hotControl, Is.Zero);
        }

        [Test]
        public void HighlightsRespectClipAndRenderedWidthForReversedSelection()
        {
            var selection = new Selection("first", "middle", "last");
            selection.Lines[1].X = 10;
            selection.Lines[1].Width = 3;
            selection.BeginMouse(Body, Mouse(3, 45), 1, new Vector2());
            selection.EndMouse(Body, Mouse(2, 5), new Vector2());
            selection.DrawHighlights(10, 40);
            var rect = EditorTrace.Draws.Single().Rect;
            Assert.That(rect.x, Is.EqualTo(10));
            Assert.That(rect.y, Is.EqualTo(20));
            Assert.That(rect.width, Is.EqualTo(3));
            Assert.That(rect.height, Is.EqualTo(10));
            EditorTrace.Draws.Clear();
            selection.Clear();
            selection.DrawHighlights(0, 100);
            Assert.That(EditorTrace.Draws, Is.Empty);
        }
    }
}
