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
                box.width -= 58;
                string name = "board.comment." + card.Id;
                bool enter = SubmitField(name);
                _editor.Comment = UiText.FieldWithPlaceholder(
                    TextBox(box),
                    name,
                    _editor.Comment,
                    "Write a comment…"
                );
                bool ready = editable && _editor.Comment.Trim().Length > 0;
                if (
                    (
                        UiControls.Button(new Rect(box.xMax + 4, row.y, 54, 32), "Post", on: ready)
                        || enter
                    ) && ready
                )
                {
                    string sent = _editor.Comment;
                    Save(
                        CardEdit(board, card, "comment", sent),
                        () =>
                        {
                            if (_editor.Card?.Id == card.Id && _editor.Comment == sent)
                                _editor.Comment = "";
                        }
                    );
                }
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
                    remove: (CanAdmin && editable) || dismissed != null
                        ? i =>
                        {
                            var item = _items[i];
                            if (item.TaskEntryKey != null)
                            {
                                dismissed.RemoveFromFeed(item.TaskEntryKey);
                                return;
                            }
                            Save(
                                BoardDiscussion.Remove(board.Id, card.Id, item.Entry, item.Activity)
                            );
                        }
                        : (System.Action<int>)null,
                    canRemove: i =>
                        _items[i].Entry != null ? CanAdmin && editable : dismissed != null
                );
        }
    }
}
