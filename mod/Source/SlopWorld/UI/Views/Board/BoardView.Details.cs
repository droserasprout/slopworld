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
                .Select(c => (c.Text, c.Author, c.TimestampMs, "Comment"))
                .Concat(
                    card.Activity.Reverse()
                        .Select(c => (c.Text, c.Author, c.TimestampMs, "Activity"))
                )
                .OrderByDescending(entry => entry.TimestampMs);
        }

        void DrawDetails(Rect panel, Wire.ProjectBoard board)
        {
            var card = _editor.Card;
            Wire.BoardEdit Edit(string action, string text = "", string kind = "") =>
                new Wire.BoardEdit
                {
                    Action = action,
                    Board = board.Id,
                    Card = card.Id,
                    Text = text,
                    Kind = kind,
                };
            Slab.Box(panel, UiTheme.Panel, UiTheme.Edge);
            panel = panel.ContractedBy(8);
            var column = board.Columns.First(c => c.Cards.Any(item => item.Id == card.Id));
            var selector = new Rect(panel.x, panel.y, Mathf.Min(250, panel.width - 40), 30);
            bool editable = !board.Archived && !_saving;
            if (editable)
                UiSelector.DrawInline(
                    selector,
                    column.Name,
                    () =>
                        board
                            .Columns.Select(c => new FloatMenuOption(
                                c.Name,
                                c.Id == column.Id
                                    ? null
                                    : () =>
                                        Save(
                                            new Wire.BoardEdit
                                            {
                                                Action = "move-card",
                                                Board = board.Id,
                                                Column = c.Id,
                                                Card = card.Id,
                                            }
                                        )
                            ))
                            .ToList()
                );
            else
                UiText.RowLabel(selector, column.Name);
            if (UiControls.Button(new Rect(panel.xMax - 32, panel.y, 32, 30), "×"))
            {
                _editor.Select(null);
                return;
            }
            panel.yMin += 36;
            bool wide = panel.width >= 620;
            var fields = panel;
            if (wide)
                fields.width = panel.width * .56f;
            void Comments(Rect area)
            {
                Slab.Fill(area, UiTheme.Well);
                area = area.ContractedBy(6);
                UiText.RowLabel(new Rect(area.x, area.y, area.width, 24), "Comments and activity");
                area.yMin += 28;
                if (!board.Archived)
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
                            UiControls.Button(
                                new Rect(box.xMax + 4, row.y, 54, 32),
                                "Post",
                                on: ready
                            ) || enter
                        ) && ready
                    )
                    {
                        string sent = _editor.Comment;
                        Save(
                            Edit("comment", sent),
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
                    if (
                        changedCard
                        || !_discussionCard.Comments.Equals(card.Comments)
                        || !_discussionCard.Activity.Equals(card.Activity)
                    )
                    {
                        if (changedCard)
                            _discussion.Reset();
                        else
                            _discussion.ClearSelection();
                        _entries = Messages(card).ToList();
                    }
                    _discussionCard = card;
                }
                _discussion.Draw(area, _entries, _entries);
            }
            _details.Draw(
                fields,
                l =>
                {
                    void Field(
                        string caption,
                        string action,
                        string original,
                        ref string draft,
                        bool multiline = false
                    )
                    {
                        l.Label(caption);
                        var row = l.GetRect(multiline ? 86 : 32);
                        var box = row;
                        if (draft != original && editable)
                            box.width -= 54;
                        string name = "board." + action + "." + card.Id;
                        draft = multiline
                            ? UiText.Area(TextBox(box), name, draft, on: !board.Archived)
                            : UiText.FieldWithPlaceholder(
                                TextBox(box),
                                name,
                                draft,
                                caption,
                                !board.Archived
                            );
                        if (
                            box.width != row.width
                            && UiControls.Button(
                                new Rect(box.xMax + 4, row.y, 50, 32),
                                "Save",
                                on: action != "title" || draft.Trim().Length > 0
                            )
                        )
                        {
                            Save(Edit(action, draft));
                        }
                    }
                    Field("Title", "title", card.Title, ref _editor.Title);
                    l.Label("Assigned to");
                    var assignee = l.GetRect(32);
                    if (editable)
                        UiSelector.DrawInline(
                            assignee,
                            AssigneeLabel(card.Assignee),
                            () => AssigneeOptions(board, value => Save(Edit("assign", value)))
                        );
                    else
                        UiText.RowLabel(assignee, AssigneeLabel(card.Assignee));
                    Field(
                        "Description",
                        "describe",
                        card.Description,
                        ref _editor.Description,
                        true
                    );
                    l.Label("Links");
                    foreach (var link in card.Links)
                    {
                        string label = link.Kind + " · " + link.Target;
                        float width = Mathf.Max(1, l.ColumnWidth - (board.Archived ? 0 : 32));
                        var row = l.GetRect(Mathf.Max(28, Text.CalcHeight(label, width)));
                        Widgets.Label(new Rect(row.x, row.y, width, row.height), label);
                        var remove = new Rect(row.xMax - 28, row.y, 28, 28);
                        if (!board.Archived)
                            TooltipHandler.TipRegion(remove, "Remove link");
                        if (!board.Archived && UiControls.Button(remove, "×", on: editable))
                            Save(Edit("unlink", link.Target, link.Kind));
                    }
                    if (!board.Archived)
                    {
                        _editor.LinkKind = UiText.FieldWithPlaceholder(
                            TextBox(l.GetRect(30)),
                            "board.kind." + card.Id,
                            _editor.LinkKind,
                            "Resource kind"
                        );
                        _editor.LinkTarget = UiText.FieldWithPlaceholder(
                            TextBox(l.GetRect(30)),
                            "board.target." + card.Id,
                            _editor.LinkTarget,
                            "Resource ID, path, or URL"
                        );
                        if (
                            UiControls.Button(
                                l.GetRect(30),
                                "Attach link",
                                on: editable
                                    && _editor.LinkKind.Trim().Length > 0
                                    && _editor.LinkTarget.Trim().Length > 0
                            )
                        )
                        {
                            var sent = _editor.LinkTarget;
                            Save(
                                Edit("link", sent, _editor.LinkKind),
                                () =>
                                {
                                    if (_editor.Card?.Id == card.Id && _editor.LinkTarget == sent)
                                        _editor.LinkTarget = "";
                                }
                            );
                        }
                    }
                    if (UiControls.Button(l.GetRect(32), "Move to board…", on: !_saving))
                        UiMenu.Open(
                            _catalog
                                .Boards.Where(b => !b.Archived)
                                .Select(b => new FloatMenuOption(
                                    b.Name,
                                    () =>
                                        UiMenu.Open(
                                            b.Columns.Select(c => new FloatMenuOption(
                                                    c.Name,
                                                    () =>
                                                        Save(
                                                            new Wire.BoardEdit
                                                            {
                                                                Action = "move-card",
                                                                Board = b.Id,
                                                                Column = c.Id,
                                                                Card = card.Id,
                                                            }
                                                        )
                                                ))
                                                .ToList()
                                        )
                                ))
                                .ToList()
                        );

                    if (
                        !board.Archived
                        && UiControls.Button(l.GetRect(30), "Delete card…", on: editable)
                    )
                        Find.WindowStack.Add(
                            ConfirmDialog.Create(
                                "Delete this card with its comments and links?",
                                () => Save(Edit("remove-card")),
                                destructive: true
                            )
                        );
                    if (!wide)
                        Comments(l.GetRect(Mathf.Max(250, panel.height * .8f)));
                }
            );
            if (wide)
            {
                panel.xMin = fields.xMax + 8;
                Comments(panel);
            }
        }
    }
}
