using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed partial class BoardView
    {
        readonly ConversationReader _discussion = new ConversationReader();
        Wire.BoardCard _discussionCard;
        List<TaskInfo> _mailbox;
        TaskInfo _standalone;
        int _dismissedCount;
        string _filter = "All",
            _shownFilter;
        List<FeedItem> _items;
        List<(string Body, string Sender, long Time, string Status)> _entries;

        void DrawDiscussion(
            Rect area,
            Wire.ProjectBoard board,
            Wire.BoardCard card,
            bool editable,
            TaskInfo standalone = null
        )
        {
            Slab.Fill(area, UiTheme.Well);
            area = area.ContractedBy(6);
            var header = new Rect(area.x, area.y, area.width, 26);
            UiText.RowLabel(header, "Feed");
            header.xMin = header.xMax - Mathf.Min(140, header.width * .65f);
            UiSelector.DrawInline(
                header,
                _filter,
                () =>
                    new[] { "All", "Comments", "Status changes" }
                        .Select(f => new FloatMenuOption(f, () => _filter = f))
                        .ToList()
            );
            area.yMin += 30;
            if (card != null && Writable(board))
            {
                var row = new Rect(area.x, area.y, area.width, 32);
                area.yMin += 36;
                var box = row;
                long entryId = _editor.EditingComment;
                bool editing = entryId != 0;
                box.width -= editing ? 86 : 58;
                string name = "board.comment." + card.Id;
                bool enter = SubmitField(name);
                var draft = UiText.FieldWithPlaceholder(
                    TextBox(box),
                    name,
                    editing ? _editor.CommentRevision : _editor.Comment,
                    "Write a comment…"
                );
                if (editing)
                    _editor.CommentRevision = draft;
                else
                    _editor.Comment = draft;
                bool ready = editable && draft.Trim().Length > 0;
                if (
                    (
                        UiControls.Button(
                            new Rect(box.xMax + 4, row.y, 54, 32),
                            editing ? "Save" : "Post",
                            on: ready
                        ) || enter
                    ) && ready
                )
                {
                    var edit = CardEdit(board, card, editing ? "edit-comment" : "comment", draft);
                    edit.EntryId = entryId;
                    Save(
                        edit,
                        () =>
                        {
                            if (_editor.Card?.Id != card.Id || _editor.EditingComment != entryId)
                                return;
                            if (editing && _editor.CommentRevision == draft)
                                _editor.EditingComment = 0;
                            else if (!editing && _editor.Comment == draft)
                                _editor.Comment = "";
                        }
                    );
                }
                if (editing && UiControls.Button(new Rect(row.xMax - 24, row.y, 24, 32), "×"))
                    _editor.EditingComment = 0;
            }
            var dismissed = Verse.Current.Game?.GetComponent<TaskFeedDismissals>();
            var mailbox = SessionHub.Instance.Tasks;
            if (
                !ReferenceEquals(card, _discussionCard)
                || !ReferenceEquals(mailbox, _mailbox)
                || !ReferenceEquals(standalone, _standalone)
                || _dismissedCount != (dismissed?.Count ?? 0)
                || _filter != _shownFilter
            )
            {
                var tasks =
                    card == null
                        ? new[] { standalone }.AsEnumerable()
                        : _editor.LinkedTasks(mailbox);
                var items = FeedItem
                    .Build(card, tasks, _filter)
                    .Where(i => dismissed?.Contains(i.TaskEntryKey) != true)
                    .ToList();
                _dismissedCount = dismissed?.Count ?? 0;
                var entries = items.Select(i => i.Message).ToList();
                if (_discussionCard?.Id != card?.Id || _filter != _shownFilter)
                    _discussion.Reset();
                if (_entries == null || !_entries.SequenceEqual(entries))
                {
                    _discussion.ClearSelection();
                    _entries = entries;
                }
                _items = items;
                _discussionCard = card;
                _mailbox = mailbox;
                _standalone = standalone;
                _shownFilter = _filter;
            }
            if (_items.Count == 0)
                UiText.RowLabel(area.TopPartPixels(30), "No entries in this feed.");
            else
                _discussion.Draw(
                    area,
                    _entries,
                    _entries,
                    openMenu: (CanAdmin && editable) || dismissed != null
                        ? i =>
                        {
                            var item = _items[i];
                            var options = new List<FloatMenuOption>();
                            if (item.Entry != null && !item.Activity)
                                options.Add(
                                    new FloatMenuOption(
                                        "Edit comment",
                                        () =>
                                        {
                                            _editor.EditingComment = item.Entry.Id;
                                            _editor.CommentRevision = item.Entry.Text;
                                        }
                                    )
                                );
                            options.Add(
                                new FloatMenuOption(
                                    item.Entry == null ? "Remove from feed"
                                        : item.Activity ? "Delete activity"
                                        : "Delete comment",
                                    () =>
                                    {
                                        if (item.TaskEntryKey != null)
                                            dismissed.RemoveFromFeed(item.TaskEntryKey);
                                        else
                                            Save(
                                                BoardDiscussion.Remove(
                                                    board.Id,
                                                    card.Id,
                                                    item.Entry,
                                                    item.Activity
                                                )
                                            );
                                    }
                                )
                            );
                            UiMenu.Open(options);
                        }
                        : (System.Action<int>)null,
                    hasMenu: i => _items[i].Entry != null ? CanAdmin && editable : dismissed != null
                );
        }
    }
}
