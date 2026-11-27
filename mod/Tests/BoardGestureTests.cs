using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class BoardGestureTests
    {
        static Wire.ProjectBoard Board()
        {
            var board = new Wire.ProjectBoard { Id = "root" };
            board.Columns.Add(
                new Wire.BoardColumn
                {
                    Id = "first",
                    Cards =
                    {
                        new Wire.BoardCard { Id = "picked" },
                        new Wire.BoardCard { Id = "anchor" },
                    },
                }
            );
            board.Columns.Add(new Wire.BoardColumn { Id = "empty" });
            return board;
        }

        [Test]
        public void EmptyDropLineRemainsAtTheTopBesideAScrolledColumn()
        {
            var board = Board();
            var viewport = new BoardViewport();
            var longColumn = board.Columns[0];
            for (int i = 0; i < 20; i++)
                longColumn.Cards.Add(new Wire.BoardCard());
            viewport.Scroll(210, 500, 800, longColumn, 280);
            Assert.That(viewport.Insertion(longColumn, 280, BoardViewport.Header), Is.EqualTo(8));
            Assert.That(viewport.Top(board.Columns[1], 280), Is.Zero);
            Assert.That(viewport.Insertion(board.Columns[1], 280, 260), Is.Zero);
            Assert.That(viewport.Left, Is.EqualTo(210));
        }

        [Test]
        public void RemoteMovePreservesDraftsAndRemovalClosesTheEditor()
        {
            var source = Board();
            var selection = new BoardSelection();
            var card = source.Columns[0].Cards[0];
            selection.Select(card);
            selection.Description = "draft description";
            selection.Comment = "draft comment";
            var destination = new Wire.ProjectBoard
            {
                Id = "project",
                Columns =
                {
                    new Wire.BoardColumn { Id = "review", Cards = { card.Clone() } },
                },
            };
            source.Columns[0].Cards.RemoveAt(0);
            var next = new Wire.BoardCatalog { Boards = { source, destination } };
            Assert.That(selection.Refresh(next).Id, Is.EqualTo("project"));
            Assert.That(selection.Description, Is.EqualTo("draft description"));
            Assert.That(selection.Comment, Is.EqualTo("draft comment"));
            destination.Columns.Clear();
            Assert.That(selection.Refresh(next), Is.Null);
            Assert.That(selection.Card, Is.Null);
        }

        [Test]
        public void TouchBodySwipesAndTouchHandleDropsOnceIntoEmptyColumn()
        {
            var board = Board();
            var g = new BoardGesture();
            g.Press("first", "picked", true, false, 10, 10);
            g.Motion(8, 50);
            Assert.That(g.Panning, Is.True);
            Assert.That(g.Release(board, true), Is.Null);
            g.Press("first", "picked", true, true, 10, 10);
            Assert.That(g.Dragging, Is.True);
            g.Target("empty", null);
            var move = g.Release(board, true);
            Assert.That(move.Column, Is.EqualTo("empty"));
            Assert.That(move.Before, Is.Empty);
            Assert.That(move.Card, Is.EqualTo("picked"));
            Assert.That(g.Release(board, true), Is.Null);
        }

        [Test]
        public void MouseRequiresMovementAndRetainsAnchorThroughReordering()
        {
            var board = Board();
            var g = new BoardGesture();
            g.Press("first", "picked", false, false, 30, 30);
            g.Motion(32, 31);
            Assert.That(g.Dragging, Is.False);
            g.Motion(49, 40);
            g.Target("first", "anchor");
            board.Columns[0].Cards.Insert(0, new Wire.BoardCard { Id = "concurrent" });
            Assert.That(g.Release(board, true).Before, Is.EqualTo("anchor"));
        }

        [TestCase("cancel")]
        [TestCase("outside")]
        [TestCase("lost anchor")]
        [TestCase("lost column")]
        public void InterruptedMoveCannotPublish(string cause)
        {
            var board = Board();
            var g = new BoardGesture();
            g.Press("first", "picked", true, true, 10, 10);
            g.Target("first", "anchor");
            if (cause == "cancel")
                g.Cancel();
            if (cause == "lost anchor")
                board.Columns[0].Cards.RemoveAt(1);
            if (cause == "lost column")
                board.Columns.RemoveAt(0);
            Assert.That(g.Release(board, cause != "outside"), Is.Null);
        }

        [Test]
        public void HeaderReleasePublishesColumnIdentityAndInsertionAnchor()
        {
            var board = Board();
            var g = new BoardGesture();
            g.Press("empty", null, true, true, 10, 10);
            g.Target("first", "first");
            var edit = g.Release(board, true);
            Assert.That(edit.Action, Is.EqualTo("move-column"));
            Assert.That(edit.Column, Is.EqualTo("empty"));
            Assert.That(edit.Before, Is.EqualTo("first"));
        }
    }
}
