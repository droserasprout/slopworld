using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static partial class LibraryView
    {
        public static void Draw(Rect body)
        {
            Lines.Clear();
            if (_scroll.HandleWheel(_list, _contentHeight)) return;
            using (FieldLifetimeScope.Push(_fieldLifetime))
            using (WidgetState.Save())
            {
                // Global definitions stay available while project-specific entries follow
                // the Library's own filter. Builtins remain in their attached menus.
                _items = SessionHub.Instance.Library
                    .Where(s => !s.Builtin && (string.IsNullOrEmpty(s.Project) || PassesProject(s.Project))).ToList();
                Templates.Clear();
                foreach (var template in SessionHub.Instance.Templates)
                {
                    var row = new LibraryItemInfo
                    {
                        Name = template.Name,
                        Project = "",
                        Text = template.Description
                    };
                    Templates[row] = template;
                    _items.Add(row);
                }
                DrawTools(body);
                _items = _items.Where(item => (_kind.Length == 0 || GroupKey(item) == _kind) &&
                    (item.Name.IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (item.Text ?? "").IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                BuildSections();
                var selected = FindSelectedRow();
                float top = body.y + 2f * (UiTheme.FieldH + Pad) + Pad;
                string error = _catalogError ?? (MainCategoryEnabled(Category.Worktrees)
                    ? WorktreeErrors.Values.FirstOrDefault() : null);
                if (error != null)
                {
                    var notice = new Rect(body.x + CellX, top, Mathf.Max(0f, body.width - CellX * 2f), UiTheme.FieldH);
                    float retryWidth = Mathf.Min(notice.width, UiLayout.BtnW("Retry", UiTheme.ButtonMinW));
                    var retry = new Rect(notice.xMax - retryWidth, top, retryWidth, notice.height);
                    if (UiButtons.Button(retry, "Retry", UiTheme.Btn.Ghost)) Refresh();
                    var text = new Rect(notice.x, top, Mathf.Max(0f, notice.width - retryWidth - Pad), notice.height);
                    UiText.PlainStatusLabel(text, error, UiTheme.Bad, GameFont.Tiny);
                    TooltipHandler.TipRegion(text, error);
                    top += notice.height + Pad;
                }
                float available = Mathf.Max(0f, body.yMax - top);
                float detailHeight = selected == null ? 0f :
                    Mathf.Min(RowH * 5f + UiTheme.FieldH * 2f + Pad * 4f,
                        Mathf.Max(0f, available - RowH * 2f));
                var selectedDetails = selected != null && detailHeight > 0f
                    ? PrepareDetails(selected) : null;
                _list = new Rect(body.x, top, body.width, Mathf.Max(0f, available - detailHeight));
                if (selected != null && detailHeight > 0f)
                    DrawDetails(new Rect(body.x + CellX, _list.yMax,
                        body.width - CellX * 2f, detailHeight), selectedDetails);
                if (Sections.Count == 0)
                {
                    _contentHeight = 0f;
                    Empty(_list);
                    return;
                }
                var list = _list;
                if (_revealSelection)
                {
                    float y = Pad;
                    foreach (var section in Sections)
                    {
                        y += HeadH;
                        if (Folded.Contains(section.Key)) continue;
                        foreach (var row in section.Rows)
                        {
                            if (row.Key == _selection)
                                _scroll.Reveal(y, RowH, list.height);
                            y += RowH;
                        }
                    }
                    _revealSelection = false;
                }
                float height = Measure();
                _contentHeight = height;
                var geometry = UiScrollBody.Measure(list, height,
                    UiScrollbarReservation.WhenNeeded);
                var view = geometry.View;

                // AgentSidebar.DrawBack skips Layout events, so use GUI rather than GUILayout.
                using (_scroll.Scope(list, view))
                {
                    float y = Pad;
                    foreach (var section in Sections)
                        y += DrawSection(view, y, section);
                }
            }
        }

        static void DrawTools(Rect body)
        {
            var field = new Rect(body.x + CellX, body.y + Pad,
                Mathf.Max(0f, body.width - CellX * 2f), UiTheme.FieldH);
            var e = Event.current;
            if ((e.type == EventType.MouseDown && !field.Contains(e.mousePosition)) ||
                (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape))
                Closed();
            string before = _query;
            _query = UiText.Field(field, "library.query", _query);
            if (before != _query) _scroll.JumpTo(Vector2.zero);
            if (_query.Length == 0 && GUI.GetNameOfFocusedControl() != "library.query")
                UiText.PlainStatusLabel(field.ContractedBy(Pad, 0f), "Search library", UiTheme.Faint, GameFont.Tiny);
            TooltipHandler.TipRegion(field, "Search library names and content");
            var filter = new Rect(field.x, field.yMax + Pad, field.width, UiTheme.FieldH);
            if (UiButtons.Button(filter, _kind.Length == 0 ? "All types ▾" : _kind + " ▾"))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("All types", () => SetKind(""))
                };
                foreach (var kind in Category.Order)
                {
                    string value = kind;
                    options.Add(new FloatMenuOption(value, () => SetKind(value)));
                }
                TerminalWindow.OpenOverPane(new UiMenu(options));
            }
        }

        static void DrawDetails(Rect r, RowDetails details)
        {
            Slab.Hairline(new Rect(r.x, r.y, r.width, 1f), UiTheme.Edge);

            // Two stacked action rows also fit the sidebar's minimum width.
            float actionsH = UiTheme.FieldH * 2f + Pad;
            float y = r.y + Pad;
            float textBottom = r.yMax - actionsH - Pad;
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                foreach (var line in details.Lines)
                {
                    if (y + RowH > textBottom) break;
                    UiText.RowLabel(new Rect(r.x, y, r.width, RowH),
                        details.IsItem ? line : OneLine(line));
                    y += RowH;
                }
                TooltipHandler.TipRegion(new Rect(r.x, r.y, r.width,
                    Mathf.Max(0f, textBottom - r.y)), details.Tooltip);
            }
            if (r.height < actionsH + Pad * 2f) return;
            var primary = new Rect(r.x, r.yMax - actionsH - Pad, r.width, UiTheme.FieldH);
            if (UiButtons.Button(primary, details.PrimaryLabel ?? "Open", UiTheme.Btn.Primary))
                details.Primary?.Invoke();
            float moreW = details.More == null ? 0f : UiTheme.FieldH;
            float secondaryW = Mathf.Max(0f, r.width - moreW - (moreW > 0f ? Pad : 0f));
            var secondary = new Rect(r.x, primary.yMax + Pad, secondaryW, UiTheme.FieldH);
            if (details.Secondary != null)
            {
                bool clicked = details.IsItem
                    ? UiButtons.Button(secondary, details.SecondaryLabel)
                    : UiButtons.Button(secondary, details.SecondaryLabel, UiTheme.Btn.Ghost);
                if (clicked) details.Secondary();
            }
            if (details.More != null && UiButtons.Button(
                    new Rect(r.xMax - moreW, secondary.y, moreW, secondary.height), "…"))
                details.More();
        }

        static float DrawSection(Rect view, float y, Section section)
        {
            float start = y;
            bool folded = Folded.Contains(section.Key);
            var headRect = new Rect(0f, y, view.width, HeadH);
            y += DrawHeading(view, headRect, section.Key, section.Key, section.Rows.Count, folded);
            if (!folded)
                foreach (var rowData in section.Rows)
                {
                    var row = new Rect(0f, y, view.width, RowH);
                    y += DrawRow(view, row, rowData);
                }
            return y - start;
        }

        static float DrawHeading(Rect view, Rect headRect, string key, string label,
            int count, bool folded)
        {
            Lines.Add(new Line { Head = true, Key = key, Rect = Screen(headRect) });

            RowChrome.Hover(headRect, false, true, RowHoverPolicy.OverlayAware);

            Rect arrow;
            using (WidgetState.Save())
            {
                GUI.color = UiTheme.Faint;
                arrow = new Rect(CellX, headRect.y + (HeadH - ArrowW) / 2f,
                    ArrowW, ArrowW);
                GUI.DrawTexture(arrow, folded ? TexButton.Reveal : TexButton.Collapse);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                float lx = arrow.xMax + UiTheme.GapXS;
                string tail = "  " + count;
                var labelRect = new Rect(lx, headRect.y, view.width - lx - CellX, HeadH);
                UiText.RowLabel(labelRect, label + tail);
            }

            Slab.Hairline(new Rect(CellX, headRect.yMax - 1f,
                view.width - CellX * 2f, 1f), UiTheme.Edge);

            TooltipHandler.TipRegion(headRect, "Click to fold.");

            return HeadH;
        }

        static float DrawRow(Rect view, Rect r, Row row)
        {
            var hitRect = Screen(r);
            RowChrome.Hover(r, row.Key == _selection, true, RowHoverPolicy.OverlayAware);
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = UiTheme.Dim;
                GUI.DrawTexture(new Rect(CellX, r.y + (RowH - ArrowW) / 2f, ArrowW, ArrowW), row.Icon);
                GUI.color = UiTheme.Lead;
                UiText.RowLabel(new Rect(CellX + ArrowW + Pad, r.y,
                    Mathf.Max(0f, r.width - CellX * 2f - ArrowW - Pad), RowH),
                    row.Label);
            }
            if (hitRect.width > 0f && hitRect.height > 0f)
            {
                TooltipHandler.TipRegion(r, RowTooltip(row));
                Lines.Add(new Line { Row = row, Rect = hitRect });
            }
            return RowH;
        }

        static string RowTooltip(Row row)
        {
            if (row.Item != null)
            {
                string scope = string.IsNullOrEmpty(row.Item.Project) ? "Global" : row.Item.Project;
                return row.Item.Name + "\n" + scope + "\n" + OneLine(row.Item.Text);
            }
            return row.TooltipJoinsPath
                ? row.Tooltip + "  -  " + row.TooltipTail
                : row.Tooltip;
        }

        // The height the rows want, measured off the same folds the draw reads.
        static float Measure()
        {
            float h = Pad;
            foreach (var section in Sections)
            {
                h += HeadH;
                if (!Folded.Contains(section.Key)) h += section.Rows.Count * RowH;
            }
            return h + Pad;
        }

        static void Empty(Rect body)
        {
            var r = new Rect(body.x + CellX, body.y + Pad, body.width - CellX * 2f, RowH * 3f);
            UiText.PlainStatusLabel(r, !SessionHub.Instance.Online
                ? $"daemon {SessionHub.Instance.Status}"
                : _query.Length > 0 || _kind.Length > 0
                    ? "No matching entries."
                    : ProjectFiltering
                    ? $"No library entries in {ProjectFilterLabel}."
                    : "No library entries yet. Press + at the foot of the panel.",
                UiTheme.Faint, GameFont.Tiny);
        }

        static string KindName(LibraryItemKind kind)
        {
            switch (kind)
            {
                case LibraryItemKind.Shell: return "Shell";
                case LibraryItemKind.Breadcrumb: return "Breadcrumb";
                case LibraryItemKind.FileAction: return "File Action";
                default: return "Prompt";
            }
        }

    }
}
