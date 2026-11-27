using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed partial class BoardView
    {
        bool _focusLink;

        readonly BoardCardLinks _cardLinks = new BoardCardLinks();

        void DrawLinkEditor(
            Listing_Standard l,
            Wire.ProjectBoard board,
            Wire.BoardCard card,
            bool editable,
            Rect viewport
        )
        {
            var row = l.GetRect(28);
            if (!_editor.AddingLink)
            {
                var add = new Rect(row.x, row.y, 28, 28);
                TooltipHandler.TipRegion(add, "Add link");
                if (UiControls.Button(add, "+", on: editable))
                {
                    _editor.AddingLink = true;
                    _focusLink = true;
                }
                return;
            }

            var links = board.Columns.SelectMany(c => c.Cards).SelectMany(c => c.Links);
            var kind = _editor.LinkKind;
            var selector = new Rect(row.x, row.y, Mathf.Min(100, row.width * .3f), row.height);
            UiSelector.DrawInline(
                selector,
                kind,
                () =>
                    new[] { "worktree", "task", "card", "branch", "file", "url" }
                        .Concat(links.Select(link => link.Kind))
                        .Distinct()
                        .Select(k => new FloatMenuOption(
                            k,
                            () =>
                            {
                                _focusLink = true;
                                _editor.LinkKind = k;
                                _editor.LinkTarget = "";
                            }
                        ))
                        .ToList()
            );
            string name = "board.target." + card.Id;
            bool enter = SubmitField(name);
            var value = row;
            value.xMin = selector.xMax + 4;
            value.xMax -= 64;
            _editor.LinkTarget = UiText.FieldWithPlaceholder(
                TextBox(value),
                name,
                _editor.LinkTarget,
                "Link value"
            );
            if (_focusLink && Event.current.type == EventType.Repaint)
            {
                GUI.FocusControl(name);
                _focusLink = false;
            }
            var target = _editor.LinkTarget.Trim();
            var linkedCard =
                kind == "card" && target.Length > 0
                    ? _cardLinks.Resolve(_catalog, board, target).Card
                    : null;
            var stored = linkedCard?.Id ?? target;
            bool ready =
                editable
                && target.Length > 0
                && (kind != "card" || linkedCard != null && linkedCard.Id != card.Id)
                && !card.Links.Any(link => link.Kind == kind && link.Target == stored);
            if (
                (
                    UiControls.Button(new Rect(value.xMax + 4, row.y, 28, 28), "✓", on: ready)
                    || enter
                ) && ready
            )
                Save(
                    CardEdit(board, card, "link", stored, kind),
                    () =>
                    {
                        if (
                            _editor.Card?.Id == card.Id
                            && _editor.LinkTarget.Trim() == target
                            && _editor.LinkKind == kind
                        )
                        {
                            _editor.LinkTarget = "";
                            _editor.AddingLink = false;
                        }
                    }
                );
            if (UiControls.Button(new Rect(row.xMax - 28, row.y, 28, 28), "×"))
            {
                _editor.AddingLink = false;
                _editor.LinkTarget = "";
                return;
            }

            var anchor = UiControls.MenuAt(value);
            if (!editable || GUI.GetNameOfFocusedControl() != name || !viewport.Contains(anchor))
                return;

            var candidates = links
                .Where(link => link.Kind == kind)
                .Select(link => (Value: link.Target, Label: link.Target));
            if (kind == "task")
                candidates = SessionHub
                    .Instance.Tasks.Select(t => (t.Id, t.Summary + " · " + t.Id))
                    .Concat(candidates);
            if (kind == "worktree")
                candidates = SidebarScopes
                    .LinkTargets(board.Id == "root" ? null : board.Name)
                    .Where(w => !string.IsNullOrEmpty(w.Path))
                    .Select(w => (w.Path, w.Project + " / " + w.Label + " · " + w.Path))
                    .Concat(candidates);
            if (kind == "card")
                candidates = _cardLinks
                    .Visible(_catalog)
                    .Where(p => p.Card.Id != card.Id)
                    .Select(p =>
                        (p.Card.Id, p.Card.Key + " · " + p.Card.Title + " · " + p.Board.Name)
                    );

            var options = candidates
                .Where(o =>
                    !string.IsNullOrEmpty(o.Value)
                    && o.Value != target
                    && (
                        o.Label.Contains(target, StringComparison.OrdinalIgnoreCase)
                        || o.Value.Contains(target, StringComparison.OrdinalIgnoreCase)
                    )
                    && !card.Links.Any(link => link.Kind == kind && link.Target == o.Value)
                )
                .GroupBy(o => o.Value)
                .Select(g => g.First())
                .Take(6)
                .ToList();
            if (options.Count == 0)
                return;
            var popup = new Rect(anchor.x, anchor.y, value.width, options.Count * 26);
            if (popup.yMax > UI.screenHeight)
                popup.y = Mathf.Max(0, anchor.y - value.height - popup.height);
            Find.WindowStack.ImmediateWindow(
                0x534C424C,
                popup,
                WindowLayer.Super,
                () =>
                {
                    for (int i = 0; i < options.Count; i++)
                    {
                        var option = options[i];
                        if (
                            UiControls.Button(new Rect(0, i * 26, popup.width, 26), option.Label)
                            && _editor.Card?.Id == card.Id
                            && _editor.LinkKind == kind
                            && _editor.AddingLink
                        )
                        {
                            var picked =
                                kind == "card"
                                    ? _cardLinks.Resolve(_catalog, board, option.Value).Card
                                    : null;
                            _editor.LinkTarget =
                                picked != null
                                && _cardLinks.Resolve(_catalog, board, picked.Key).Card?.Id
                                    == picked.Id
                                    ? picked.Key
                                    : option.Value;
                            GUI.FocusControl(null);
                        }
                    }
                },
                true,
                false,
                0f
            );
        }
    }
}
