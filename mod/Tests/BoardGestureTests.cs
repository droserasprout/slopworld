using System.Linq;
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
        public void CardLinksKeepTheirDestinationAcrossDuplicateKeysAndRenames()
        {
            var catalog = new Wire.BoardCatalog();
            var first = Board();
            var second = Board();
            second.Id = "other";
            var local = first.Columns[0].Cards[0];
            var remote = second.Columns[0].Cards[0];
            remote.Id = "remote";
            local.Key = remote.Key = "SW-3";
            catalog.Boards.Add(first);
            catalog.Boards.Add(second);
            var links = new BoardCardLinks();
            Assert.That(links.Resolve(catalog, first, "sw-3").Card, Is.SameAs(local));
            Assert.That(links.Resolve(catalog, new Wire.ProjectBoard(), "SW-3").Card, Is.Null);
            var updated = catalog.Clone();
            updated.Boards[1].Columns[0].Cards[0].Key = "NEW-3";
            Assert.That(links.Resolve(updated, first, remote.Id).Card.Key, Is.EqualTo("NEW-3"));
        }

        [Test]
        public void TimelineInterleavesEditsAndCommentsAndTargetsStableEntryIds()
        {
            var card = new Wire.BoardCard { Id = "card" };
            foreach (var author in new[] { "agent", "host", "host", "agent" })
                card.Activity.Add(
                    new Wire.BoardComment
                    {
                        Id = card.Activity.Count + 10,
                        TimestampMs = card.Activity.Count * 10,
                        Author = author,
                        Text = "Changed the title",
                    }
                );
            card.Comments.Add(new Wire.BoardComment { Id = 20, TimestampMs = 15 });
            Assert.That(BoardDiscussion.Timeline(card, false).Single().Entry.Id, Is.EqualTo(20));
            Assert.That(
                BoardDiscussion.Timeline(card, true).Select(e => e.Entry.Id),
                Is.EqualTo(new[] { 13, 12, 20, 11, 10 })
            );
            var entry = card.Activity[1];
            var remove = BoardDiscussion.Remove("root", card.Id, entry, true);
            Assert.That(
                (remove.Action, remove.Board, remove.Card, remove.Text),
                Is.EqualTo(("remove-activity", "root", "card", "11"))
            );
            card.Activity.RemoveAt(1);
            Assert.That(card.Activity[1].Id, Is.EqualTo(12));
            var comment = new Wire.BoardComment { Id = 22 };
            remove = BoardDiscussion.Remove("root", card.Id, comment, false);
            Assert.That((remove.Action, remove.Text), Is.EqualTo(("remove-comment", "22")));
        }

        [Test]
        public void FeedFiltersPreserveTimelineOrderAndSkipUnavailableTasks()
        {
            var card = new Wire.BoardCard();
            card.Comments.Add(new Wire.BoardComment { Text = "comment", TimestampMs = 2 });
            card.Activity.Add(new Wire.BoardComment { Text = "edit", TimestampMs = 4 });
            var task = new TaskInfo
            {
                Body = "request",
                CreatedMs = 1,
                Note = "result",
                UpdatedMs = 3,
            };
            var tasks = new[] { task, null };
            string[] Bodies(string filter) =>
                FeedItem.Build(card, tasks, filter).Select(i => i.Message.Body).ToArray();
            Assert.That(
                Bodies("All"),
                Is.EqualTo(new[] { "edit", "result", "comment", "request" })
            );
            Assert.That(Bodies("Comments"), Is.EqualTo(new[] { "comment" }));
            Assert.That(Bodies("Activity"), Is.EqualTo(new[] { "edit" }));
            task.History.Add(new TaskUpdateInfo { Note = "done", TimestampMs = 3 });
            Assert.That(Bodies("Tasks"), Is.EqualTo(new[] { "done", "request" }));
            Assert.That(FeedItem.Build(null, tasks, "All").Count(), Is.EqualTo(2));
        }

        [Test]
        public void LinkedAssignmentsResolveOnlyFromTheVisibleTaskMailbox()
        {
            var board = Board();
            var selection = new BoardSelection();
            var card = board.Columns[0].Cards[0];
            card.Links.Add(new Wire.BoardLink { Kind = "task", Target = "completed" });
            card.Links.Add(new Wire.BoardLink { Kind = "task", Target = "removed" });
            selection.Select(card);
            var task = new TaskInfo
            {
                Id = "completed",
                Status = DelegatedTaskStatus.Done,
                Note = "the result",
            };
            var linked = selection.LinkedTasks(new[] { task }).ToList();
            Assert.That(linked[0].Note, Is.EqualTo("the result"));
            Assert.That(linked[1], Is.Null);
            Assert.That(selection.Card.Id, Is.EqualTo(card.Id));
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
            selection.Title = "draft title";
            selection.Description = "draft description";
            selection.Comment = "draft comment";
            var destination = source.Columns[1];
            destination.Cards.Add(card.Clone());
            destination.Cards[0].Assignee = "agent";
            source.Columns[0].Cards.RemoveAt(0);
            var next = new Wire.BoardCatalog { Boards = { source } };
            Assert.That(selection.Refresh(next).Id, Is.EqualTo("root"));
            Assert.That(selection.Title, Is.EqualTo("draft title"));
            Assert.That(selection.Card.Assignee, Is.EqualTo("agent"));
            Assert.That(selection.Description, Is.EqualTo("draft description"));
            Assert.That(selection.Comment, Is.EqualTo("draft comment"));
            destination.Cards.Clear();
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
        [TestCase("other board")]
        public void InterruptedMoveCannotPublish(string cause)
        {
            var board = Board();
            var g = new BoardGesture();
            g.Press("first", "picked", true, true, 10, 10);
            g.Target("first", "anchor");
            if (cause == "other board")
                board = new Wire.ProjectBoard
                {
                    Id = "other",
                    Columns = { new Wire.BoardColumn { Id = "other column" } },
                };
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
