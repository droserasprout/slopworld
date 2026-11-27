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
        List<(string Body, string Sender, long Time, string Status)> _entries;

        static IEnumerable<(string Body, string Sender, long Time, string Status)> Messages(
            Wire.BoardCard card
        )
        {
            return card
                .Comments.Reverse()
                .Select(c => (c.Text, c.Author, c.TimestampMs, "Comment"));
        }

        void DrawDiscussion(Rect area, Wire.ProjectBoard board, Wire.BoardCard card, bool editable)
        {
            Slab.Fill(area, UiTheme.Well);
            area = area.ContractedBy(6);
            if (
                UiControls.Button(
                    new Rect(area.x, area.y, area.width / 2, 24),
                    _activity ? "Hide activity" : "Show activity"
                )
            )
                _activity = !_activity;
            if (
                CanAdmin
                && editable
                && card.Comments.Count > 0
                && UiControls.Button(
                    new Rect(area.center.x, area.y, area.width / 2, 24),
                    "Remove comment…"
                )
            )
                UiMenu.Open(
                    card.Comments.Reverse()
                        .Select(c => new FloatMenuOption(
                            "#" + c.Id + " " + c.Author + " · " + c.Text,
                            () => Save(BoardDiscussion.Remove(board.Id, card.Id, c, false))
                        ))
                        .ToList()
                );
            area.yMin += 28;
            if (Writable(board))
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
            if (!ReferenceEquals(_discussionCard, card))
            {
                bool changedCard = _discussionCard?.Id != card.Id;
                if (changedCard || !_discussionCard.Comments.Equals(card.Comments))
                {
                    if (changedCard)
                        _discussion.Reset();
                    else
                        _discussion.ClearSelection();
                    _entries = Messages(card).ToList();
                }
                _discussionCard = card;
            }
            if (_activity)
            {
                var events = area;
                events.height *= .45f;
                area.yMin = events.yMax + 4;
                _events.Draw(
                    events,
                    l =>
                    {
                        foreach (var (end, start) in BoardDiscussion.ActivityGroups(card))
                        {
                            var first = card.Activity[end];
                            int count = end - start + 1;
                            var row = l.GetRect(23);
                            UiText.PlainStatusLabel(
                                row,
                                first.Author + " · " + first.Text + (count > 1 ? " ×" + count : ""),
                                UiTheme.Dim
                            );
                            if (Widgets.ButtonInvisible(row) && !_expanded.Remove(first.Id))
                                _expanded.Add(first.Id);
                            if (_expanded.Contains(first.Id))
                                for (int j = end; j >= start; j--)
                                {
                                    var entry = card.Activity[j];
                                    if (
                                        UiControls.Button(
                                            l.GetRect(24),
                                            "#"
                                                + entry.Id
                                                + " · "
                                                + ConversationReader.Timestamp(entry.TimestampMs)
                                                + (CanAdmin && editable ? " · Remove" : ""),
                                            on: CanAdmin && editable
                                        )
                                    )
                                        Save(
                                            BoardDiscussion.Remove(board.Id, card.Id, entry, true)
                                        );
                                }
                        }
                    }
                );
            }
            _discussion.Draw(area, _entries, _entries);
        }
    }
}
