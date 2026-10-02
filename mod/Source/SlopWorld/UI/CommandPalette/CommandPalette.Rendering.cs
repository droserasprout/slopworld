using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public partial class CommandPalette
    {
        public override void DoWindowContents(Rect rect)
        {
            using (WidgetState.Save())
            using (FieldLifetimeScope.Push(_fieldLifetime))
            {
                // A raised rectangular surface: this is an instrument panel, not a vanilla menu.
                Slab.Box(rect, UiTheme.PopoverBg, UiTheme.Edge);

                var inputRect = new Rect(rect.x + Pad, rect.y + Pad,
                    rect.width - Pad * 2, InputH);

                float listTop = inputRect.yMax + UiTheme.GapXS;
                var listRect = new Rect(rect.x + Pad, listTop,
                    rect.width - Pad * 2, rect.yMax - listTop - Pad);
                // Set before DrawInput, which reads it when a key scrolls the selection.
                _listH = listRect.height;

                DrawInput(inputRect);

                if (_mode == Mode.Sub) DrawSubList(listRect);
                else DrawCommandList(listRect);
            }
        }

        public override void PostClose()
        {
            _fieldLifetime.Cancel();
            base.PostClose();
        }

        // --------------------------------------------------------------- command list

        void DrawCommandList(Rect r)
        {
            if (_matches.Count == 0)
            {
                GUI.color = UiTheme.Faint;
                UiText.RowLabel(r, _filter.Length > 0
                    ? "No matching commands"
                    : "No commands available", TextAnchor.MiddleCenter);
                GUI.color = Color.white;
                return;
            }

            // Total height, with a header where the group changes. Recent entries carry
            // their own group so they read as one block ahead of the rest.
            float totalH = 0f;
            string prev = null;
            for (int i = 0; i < _matches.Count; i++)
            {
                if (Grouped)
                {
                    string group = GroupOf(i);
                    if (group != prev) { totalH += GroupH; prev = group; }
                }
                totalH += RowH;
            }

            var view = new Rect(0f, 0f, r.width - UiTheme.ScrollbarW, totalH);

            using (_scroll.Scope(r, view))
            {

                float y = 0f;
                prev = null;
                for (int i = 0; i < _matches.Count; i++)
                {
                    string group = Grouped ? GroupOf(i) : null;
                    if (group != null && group != prev)
                    {
                        var header = new Rect(0f, y, view.width, GroupH);
                        GUI.color = UiTheme.Faint;
                        Text.Font = GameFont.Tiny;
                        UiText.RowLabel(header, group.ToUpperInvariant());
                        Text.Font = GameFont.Small;
                        GUI.color = Color.white;
                        y += GroupH;
                        prev = group;
                    }

                    var row = new Rect(0f, y, view.width, RowH);
                    bool selected = i == _selectedIndex;

                    RowChrome.Hover(row, selected, true, RowHoverPolicy.Local,
                        RowSelectionStyle.Palette);

                    if (UiButtons.RowButton(row))
                    {
                        _selectedIndex = i;
                        if (_matches[i].Command.SubAction != null) EnterSub(_matches[i].Command);
                        else Execute(_matches[i].Command);
                    }

                    GUI.color = selected ? UiTheme.Lead : UiTheme.Name;
                    UiText.RowLabel(
                        new Rect(row.x + UiTheme.FieldPadX, row.y + UiTheme.FieldPadY,
                            view.width - UiTheme.FieldPadX * 2f,
                            RowH - UiTheme.FieldPadY * 2f),
                        _matches[i].Label);
                    GUI.color = Color.white;

                    y += RowH;
                }

            }
        }

        // A filtered list shows matches in rank order, with the best match first.
        // Omit group headings to make this order clear.
        bool Grouped => _filter.Length == 0;

        // Without a filter, recent entries appear first under "Recently".
        // Other entries appear under their own categories.
        string GroupOf(int index) =>
            index < _recentInList ? "Recently" : _matches[index].Command.Group;

        // --------------------------------------------------------------- sub list

        void DrawSubList(Rect r)
        {
            var options = _subShown;

            if (options.Count == 0)
            {
                GUI.color = UiTheme.Faint;
                UiText.RowLabel(r, _subHasFilter ? "No matches" : "Nothing available",
                    TextAnchor.MiddleCenter);
                GUI.color = Color.white;
                return;
            }

            _subIndex = Mathf.Clamp(_subIndex, 0, options.Count - 1);

            float totalH = options.Count * RowH;
            var view = new Rect(0f, 0f, r.width - UiTheme.ScrollbarW, totalH);

            using (_scroll.Scope(r, view))
            {

                float y = 0f;
                for (int i = 0; i < options.Count; i++)
                {
                    var row = new Rect(0f, y, view.width, RowH);
                    bool selected = i == _subIndex;

                    RowChrome.Hover(row, selected, options[i].O.Enabled, RowHoverPolicy.Local,
                        RowSelectionStyle.Palette);

                    if (UiButtons.RowButton(row))
                    {
                        _subIndex = i;
                        ExecuteSub();
                    }

                    float left = row.x + UiTheme.FieldPadX;

                    // The checkbox goes before the label, the way a settings page draws one,
                    // and the label starts after it. Rows without one keep the whole line:
                    // a list is all ticks or none, so nothing is left hanging.
                    var box = options[i].O.Checked;
                    if (box.HasValue)
                    {
                        UiControls.TickBox(new Rect(left, row.y, UiControls.TickW, RowH),
                            box.Value);
                        left += UiControls.TickColW;
                    }

                    if (!options[i].O.Enabled)
                    {
                        GUI.color = UiTheme.Off;
                    }
                    else
                    {
                        GUI.color = selected ? UiTheme.Lead : UiTheme.Name;
                    }

                    UiText.RowLabel(
                        new Rect(left, row.y + UiTheme.FieldPadY,
                            row.xMax - UiTheme.FieldPadX - left,
                            RowH - UiTheme.FieldPadY * 2f),
                        options[i].Label);
                    GUI.color = Color.white;

                    y += RowH;
                }

            }
        }
    }
}
