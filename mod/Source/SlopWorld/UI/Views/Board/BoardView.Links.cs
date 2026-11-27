using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed partial class BoardView
    {
        void DrawLinkEditor(
            Listing_Standard l,
            Wire.ProjectBoard board,
            Wire.BoardCard card,
            bool editable
        )
        {
            var row = l.GetRect(28);
            if (!_editor.AddingLink)
            {
                var add = new Rect(row.x, row.y, 28, 28);
                TooltipHandler.TipRegion(add, "Add link");
                if (UiControls.Button(add, "+", on: editable))
                    _editor.AddingLink = true;
                return;
            }

            var links = board.Columns.SelectMany(c => c.Cards).SelectMany(c => c.Links);
            var kind = _editor.LinkKind;
            var selector = new Rect(row.x, row.y, Mathf.Min(100, row.width * .3f), row.height);
            UiSelector.DrawInline(
                selector,
                kind,
                () =>
                    new[] { "worktree", "task", "branch", "file", "url" }
                        .Concat(links.Select(link => link.Kind))
                        .Distinct()
                        .Select(k => new FloatMenuOption(
                            k,
                            () =>
                            {
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
            var target = _editor.LinkTarget.Trim();
            bool ready = editable && target.Length > 0;
            if (
                (
                    UiControls.Button(new Rect(value.xMax + 4, row.y, 28, 28), "✓", on: ready)
                    || enter
                ) && ready
            )
                Save(
                    CardEdit(board, card, "link", target, kind),
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

            var candidates = links
                .Where(link => link.Kind == kind)
                .Select(link => (Value: link.Target, Label: link.Target));
            if (kind == "task")
                candidates = SessionHub
                    .Instance.Tasks.Select(t => (t.Id, t.Summary + " · " + t.Id))
                    .Concat(candidates);
            if (kind == "worktree")
                candidates = candidates.Concat(
                    SessionHub.Instance.Sessions.Select(s => (s.Worktree, s.Worktree))
                );
            foreach (
                var option in candidates
                    .Where(o =>
                        !string.IsNullOrEmpty(o.Value)
                        && o.Value != target
                        && o.Label.Contains(target, StringComparison.OrdinalIgnoreCase)
                        && !card.Links.Any(link => link.Kind == kind && link.Target == o.Value)
                    )
                    .GroupBy(o => o.Value)
                    .Select(g => g.First())
                    .Take(5)
            )
                if (UiControls.Button(l.GetRect(24), option.Label, on: editable))
                    _editor.LinkTarget = option.Value;
        }
    }
}
