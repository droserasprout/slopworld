using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed partial class BoardView
    {
        bool _activity;
        readonly HashSet<long> _expanded = new HashSet<long>();
        readonly ScrollableListing _events = new ScrollableListing();

        void SelectCard(Wire.BoardCard card)
        {
            _activity = false;
            _expanded.Clear();
            _editor.Select(card);
        }

        static Wire.BoardEdit CardEdit(
            Wire.ProjectBoard board,
            Wire.BoardCard card,
            string action,
            string text = "",
            string kind = ""
        ) =>
            new Wire.BoardEdit
            {
                Action = action,
                Board = board.Id,
                Card = card.Id,
                Text = text,
                Kind = kind,
            };

        void DrawDetails(Rect panel, Wire.ProjectBoard board)
        {
            var card = _editor.Card;
            Slab.Box(panel, UiTheme.Panel, UiTheme.Edge);
            panel = panel.ContractedBy(8);
            var column = board.Columns.First(c => c.Cards.Any(item => item.Id == card.Id));
            var selector = new Rect(panel.x, panel.y, Mathf.Min(250, panel.width - 40), 30);
            bool editable = Writable(board) && !_saving;
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
            var discussion = panel;
            if (panel.width >= 620)
            {
                discussion.xMin += panel.width * .57f;
                panel.xMax = discussion.xMin - 8;
            }
            else
            {
                discussion.yMin += panel.height * .52f;
                panel.yMax = discussion.yMin - 6;
            }
            DrawDiscussion(discussion, board, card, editable);
            _details.Draw(
                panel,
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
                            ? UiText.Area(TextBox(box), name, draft, on: Writable(board))
                            : UiText.FieldWithPlaceholder(
                                TextBox(box),
                                name,
                                draft,
                                caption,
                                Writable(board)
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
                            Save(CardEdit(board, card, action, draft));
                        }
                    }
                    Field(card.Key, "title", card.Title, ref _editor.Title);
                    l.Label("Assigned to");
                    var assignee = l.GetRect(32);
                    if (editable)
                        UiSelector.DrawInline(
                            assignee,
                            AssigneeLabel(card.Assignee),
                            () =>
                                AssigneeOptions(
                                    board,
                                    value => Save(CardEdit(board, card, "assign", value))
                                )
                        );
                    else
                        UiText.RowLabel(assignee, AssigneeLabel(card.Assignee));
                    l.Label("Links");
                    foreach (var link in card.Links)
                    {
                        string label = link.Kind + ":" + link.Target;
                        float width = Mathf.Max(1, l.ColumnWidth - (!Writable(board) ? 0 : 32));
                        var row = l.GetRect(
                            Mathf.Max(28, Text.CalcHeight(label, Mathf.Max(1, width - 6)) + 6)
                        );
                        var badge = new Rect(
                            row.x,
                            row.y,
                            Mathf.Min(width, Text.CalcSize(label).x + 12),
                            row.height
                        );
                        Slab.Fill(badge, UiTheme.Well);
                        Widgets.Label(badge.ContractedBy(3), label);
                        var remove = new Rect(row.xMax - 28, row.y, 28, 28);
                        if (Writable(board))
                            TooltipHandler.TipRegion(remove, "Remove link");
                        if (Writable(board) && UiControls.Button(remove, "×", on: editable))
                            Save(CardEdit(board, card, "unlink", link.Target, link.Kind));
                    }
                    Field(
                        "Description",
                        "describe",
                        card.Description,
                        ref _editor.Description,
                        true
                    );
                    if (Writable(board))
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
                                CardEdit(board, card, "link", sent, _editor.LinkKind),
                                () =>
                                {
                                    if (_editor.Card?.Id == card.Id && _editor.LinkTarget == sent)
                                        _editor.LinkTarget = "";
                                }
                            );
                        }
                    }
                    l.Label("Tasks");
                    DrawTasks(l, board, card, editable);
                    if (
                        Writable(board)
                        && CanAdmin
                        && UiControls.Button(l.GetRect(30), "Delete card…", on: editable)
                    )
                        Find.WindowStack.Add(
                            ConfirmDialog.Create(
                                "Delete this card with its comments and links?",
                                () => Save(CardEdit(board, card, "remove-card")),
                                destructive: true
                            )
                        );
                }
            );
        }
    }
}
