using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed partial class BoardView
    {
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
            Slab.Fill(panel, UiTheme.Panel);
            panel = panel.ContractedBy(5);
            if (UiControls.Button(new Rect(panel.x, panel.y, 65, 30), "‹ Back"))
            {
                _editor.Select(null);
                return;
            }
            UiText.RowLabel(new Rect(panel.x + 71, panel.y, panel.width - 71, 30), card.Title);
            panel.yMin += 34;
            if (!board.Archived)
            {
                var composer = new Rect(panel.x, panel.yMax - 36, panel.width - 65, 36);
                _editor.Comment = UiText.FieldWithPlaceholder(
                    TextBox(composer),
                    "board.comment." + card.Id,
                    _editor.Comment,
                    "Add a comment…"
                );
                if (
                    UiControls.Button(
                        new Rect(composer.xMax + 4, composer.y, 61, 36),
                        "Post",
                        on: !_saving && _editor.Comment.Trim().Length > 0
                    )
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
                panel.yMax = composer.y - 4;
            }
            _details.Draw(
                panel,
                l =>
                {
                    bool editable = !board.Archived && !_saving;
                    if (
                        !board.Archived
                        && UiControls.Button(l.GetRect(30), "Rename card…", on: editable)
                    )
                        Prompt("Card title", card.Title, s => Save(Edit("title", s)));
                    l.Label("Description");
                    _editor.Description = UiText.Area(
                        board.Archived ? l.GetRect(86) : TextBox(l.GetRect(86)),
                        "board.description." + card.Id,
                        _editor.Description,
                        on: !board.Archived
                    );
                    if (
                        !board.Archived
                        && UiControls.Button(
                            l.GetRect(30),
                            "Save description",
                            on: editable && _editor.Description != card.Description
                        )
                    )
                        Save(Edit("describe", _editor.Description));
                    l.Label(
                        "Assigned to " + (card.Assignee.Length == 0 ? "nobody" : card.Assignee)
                    );
                    if (
                        !board.Archived && UiControls.Button(l.GetRect(30), "Assign…", on: editable)
                    )
                        Prompt("Assignee", card.Assignee, s => Save(Edit("assign", s)));
                    if (
                        card.Assignee.Length > 0
                        && !board.Archived
                        && UiControls.Button(l.GetRect(30), "Clear assignment", on: editable)
                    )
                        Save(Edit("assign"));
                    l.Label("Links");
                    foreach (var link in card.Links)
                    {
                        l.Label(link.Kind + " · " + link.Target);
                        if (
                            !board.Archived
                            && UiControls.Button(l.GetRect(28), "Remove link", on: editable)
                        )
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
                    l.Label("Comments");
                    foreach (var comment in card.Comments)
                        l.Label(comment.Author + " · " + comment.Text);
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
                }
            );
        }
    }
}
