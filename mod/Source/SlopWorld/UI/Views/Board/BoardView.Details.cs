using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed partial class BoardView
    {
        CardWindow _cardWindow;

        sealed class CardWindow : UiWindow
        {
            readonly BoardView _view;

            public CardWindow(BoardView view)
            {
                _view = view;
                resizeable = true;
            }

            public override Vector2 InitialSize => new Vector2(920f, 600f);
            protected override Vector2 MinimumSize => new Vector2(320f, 300f);

            protected override void DoBody(Rect body)
            {
                var board = _view.Board;
                if (_view.Active && board != null && _view._editor.Card != null)
                {
                    if (Event.current.rawType == EventType.MouseDown)
                        _view.TouchTextRequested = false;
                    using (WidgetState.Save())
                    {
                        UiLayout.Title(TitleRect(body), _view._editor.Card.Key);
                        body.yMin += UiTheme.HeaderH;
                        _view.DrawDetails(body, board);
                    }
                }
            }

            public override void PostClose()
            {
                _view._cardWindow = null;
                if (_view.Active && _view.Board != null)
                    _view._editor.Select(null);
                base.PostClose();
            }
        }

        void SelectCard(Wire.BoardCard card)
        {
            _filter = "All";
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
            var viewport = new Rect(UI.GUIToScreenPoint(panel.position), panel.size);
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
                    var worktrees = card.Links.Any(link => link.Kind == "worktree")
                        ? SidebarScopes.LinkTargets(ProjectName(board)).ToList()
                        : null;
                    foreach (var link in card.Links)
                    {
                        var task =
                            link.Kind == "task"
                                ? SessionHub.Instance.Tasks.FirstOrDefault(t => t.Id == link.Target)
                                : null;
                        var destination =
                            link.Kind == "card"
                                ? _cardLinks.Resolve(_catalog, board, link.Target)
                                : default;
                        string label =
                            destination.Card != null
                                ? destination.Card.Key + " · " + destination.Card.Title
                            : link.Kind == "worktree"
                                ? "worktree:"
                                    + (
                                        worktrees.FirstOrDefault(w => w.Path == link.Target)?.Label
                                        ?? link.Target
                                    )
                            : task == null ? link.Kind + ":" + link.Target
                            : task.Summary + " · " + TaskInfo.StatusText(task.Status);
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
                        if (destination.Card != null)
                        {
                            if (UiControls.Button(badge, label))
                            {
                                if (destination.Board.Id != _board)
                                    PickBoard(destination.Board.Id);
                                SelectCard(destination.Card);
                                return;
                            }
                        }
                        else if (task == null)
                            Widgets.Label(badge.ContractedBy(3), label);
                        else if (UiControls.Button(badge, label))
                            TaskDetailView.Open(task, this);
                        var remove = new Rect(row.xMax - 28, row.y, 28, 28);
                        if (Writable(board))
                            TooltipHandler.TipRegion(remove, "Remove link");
                        if (Writable(board) && UiControls.Button(remove, "×", on: editable))
                            Save(CardEdit(board, card, "unlink", link.Target, link.Kind));
                    }
                    if (Writable(board))
                        DrawLinkEditor(l, board, card, editable, viewport);
                    Field(
                        "Description",
                        "describe",
                        card.Description,
                        ref _editor.Description,
                        true
                    );

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
