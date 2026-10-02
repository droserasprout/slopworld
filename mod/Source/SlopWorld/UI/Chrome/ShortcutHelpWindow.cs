using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A workspace reference for both live key bindings and shortcuts that deliberately do not
    // go through KeyBindingDef. It shares TerminalWindow's fullscreen chrome with Settings and
    // terminal views. The map-only `?` shortcut still never competes with an agent pane.
    public sealed class ShortcutHelpWindow : ContentView
    {
        sealed class ShortcutRow
        {
            public readonly string Key;
            public readonly string Action;

            public ShortcutRow(string key, string action)
            {
                Key = key;
                Action = action;
            }
        }

        sealed class ShortcutGroup
        {
            public readonly string Title;
            public readonly List<ShortcutRow> Rows;

            public ShortcutGroup(string title, IEnumerable<ShortcutRow> rows)
            {
                Title = title;
                Rows = rows.ToList();
            }
        }

        const float KeyColumnMin = 104f;
        const float KeyColumnMax = 160f;
        static float GroupGap => UiTheme.GapXS;

        readonly SmoothScroll _scroll = new SmoothScroll();

        public override string Title => "Keyboard shortcuts";

        public static bool CanOpen => ModProfile.Ok && !Cutscene.Playing &&
            Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null &&
            Find.WindowStack != null;

        public static void Toggle()
        {
            if (!CanOpen) return;
            TerminalWindow.ToggleContent(() => new ShortcutHelpWindow());
        }

        // GameComponentOnGUI sees the map event before WindowStack and before a focused
        // vanilla control. Keep the host guard here too for callers outside TerminalHotkeys.
        public static bool HandleMapKey(Event e)
        {
            if (e == null || e.type != EventType.KeyDown || !CanOpen || !IsHelpKey(e) ||
                Find.WindowStack?.WindowOfType<TerminalWindow>() != null)
                return false;

            Toggle();
            e.Use();
            return true;
        }

        // Once help is hosted by TerminalWindow, the map component sees the next `?` while
        // that host is already present. Preserve the old show/hide shortcut without letting
        // the same key reach a backing agent.
        public static bool HandleContentKey(Event e)
        {
            if (e == null || !IsKeyDown(e) || !IsHelpKey(e) ||
                TerminalWindow.ShowingAs<ShortcutHelpWindow>() == null)
                return false;

            Find.WindowStack?.WindowOfType<TerminalWindow>()?.Leave();
            e.Use();
            return true;
        }

        static bool IsKeyDown(Event e) => e.type == EventType.KeyDown ||
            (e.type == EventType.Used && e.rawType == EventType.KeyDown);

        static bool IsHelpKey(Event e)
        {
            if (e.control || e.alt || e.command) return false;
            // The character catches non-US layouts. Slash+Shift covers Unity players that
            // report the physical key but lose the resolved question-mark character.
            return e.character == '?' || (e.keyCode == KeyCode.Slash && e.shift);
        }

        public override void Closed() => _scroll.JumpTo(Vector2.zero);

        public override void Draw(Rect body)
        {
            var panel = OptionsView.Band(body);
            Slab.Box(panel, UiTheme.WindowBg, UiTheme.Edge);
            var rect = panel.ContractedBy(UiTheme.GapM);
            UiLayout.Title(rect, Title);

            var list = new Rect(rect.x, rect.y + UiTheme.HeaderH + UiTheme.GapS,
                rect.width, Mathf.Max(0f, rect.height - UiTheme.HeaderH - UiTheme.GapS));
            var groups = Groups();
            float keyWidth = KeyWidth(groups, list.width);
            float total = ContentHeight(groups);
            var view = new Rect(0f, 0f, Mathf.Max(1f, list.width - UiTheme.ScrollbarW),
                Mathf.Max(total, list.height));

            using (_scroll.Scope(list, view))
            {
                float y = 0f;
                foreach (var group in groups)
                {
                    y = DrawGroup(group, y, view.width, keyWidth);
                }
            }
        }

        static float KeyWidth(List<ShortcutGroup> groups, float available)
        {
            float widest = KeyColumnMin;
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                foreach (var group in groups)
                    foreach (var row in group.Rows)
                        widest = Mathf.Max(widest, UiTheme.Wide(row.Key) +
                            UiTheme.FieldPadX * 2f);
            }
            float minimum = Mathf.Min(KeyColumnMin, Mathf.Max(1f, available));
            float maximum = Mathf.Min(KeyColumnMax, Mathf.Max(1f, available - UiTheme.GapS));
            return Mathf.Clamp(widest, minimum, Mathf.Max(minimum, maximum));
        }

        static float ContentHeight(List<ShortcutGroup> groups)
        {
            float total = 0f;
            foreach (var group in groups)
                total += UiTheme.TinyRowH +
                    group.Rows.Count * UiTheme.PaletteRowH + GroupGap;
            return total;
        }

        static float DrawGroup(ShortcutGroup group, float y, float width, float keyWidth)
        {
            var heading = new Rect(0f, y, width, UiTheme.TinyRowH);
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Tiny;
                GUI.color = UiTheme.Faint;
                UiText.RowLabel(heading, group.Title.ToUpperInvariant());
                Slab.Hairline(new Rect(0f, heading.yMax - 1f, width, 1f), UiTheme.Edge);
            }
            y += UiTheme.TinyRowH;

            for (int i = 0; i < group.Rows.Count; i++)
            {
                DrawRow(group.Rows[i], new Rect(0f, y, width, UiTheme.PaletteRowH), keyWidth);
                y += UiTheme.PaletteRowH;
            }

            return y + GroupGap;
        }

        static void DrawRow(ShortcutRow shortcut, Rect row, float keyWidth)
        {
            if (Mouse.IsOver(row)) Slab.Fill(row, UiTheme.RowBg);

            var key = new Rect(row.x, row.y + 2f, keyWidth, row.height - 4f);
            Slab.Box(key, UiTheme.Well, UiTheme.Edge);
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                GUI.color = UiTheme.Lead;
                UiText.RowLabel(key.ContractedBy(UiTheme.FieldPadX, 0f), shortcut.Key);
                GUI.color = UiTheme.Name;
                var action = new Rect(key.xMax + UiTheme.GapS, row.y,
                    row.xMax - key.xMax - UiTheme.GapS, row.height);
                UiText.RowLabel(action, shortcut.Action);
            }
        }

        static List<ShortcutGroup> Groups()
        {
            var groups = new List<ShortcutGroup>();
            var categories = DefDatabase<KeyBindingCategoryDef>.AllDefs
                .OrderBy(c => c.label ?? c.defName)
                .ThenBy(c => c.defName);

            foreach (var category in categories)
            {
                var rows = DefDatabase<KeyBindingDef>.AllDefs
                    .Where(b => b.category == category && StripKeys.Kept(b))
                    .OrderBy(b => b.defName)
                    .Select(b => new ShortcutRow(ShortcutLabels.Binding(b), b.label));
                var list = rows.ToList();
                if (list.Count > 0)
                    groups.Add(new ShortcutGroup("Configurable · " + (category.label ?? category.defName),
                        list));
            }

            groups.Add(new ShortcutGroup("Built-in · Navigation", new[]
            {
                new ShortcutRow("Alt+Z", "Previous session"),
                new ShortcutRow("Alt+X", "Next session"),
                new ShortcutRow("Ctrl+sidebar key", "Focus that view’s last target using its current binding"),
            }));
            groups.Add(new ShortcutGroup("Built-in · Terminal", new[]
            {
                new ShortcutRow("Escape", "Forward to the agent in the active terminal pane"),
                new ShortcutRow("Shift+Escape", "Close the terminal"),
                new ShortcutRow("Alt+1..9, Alt+0", "Select an agent by sidebar position"),
                new ShortcutRow("Shift+Enter", "Send a newline without submitting"),
                new ShortcutRow("Ctrl+C", "Copy selected text. Without a selection, send SIGINT."),
                new ShortcutRow("Ctrl+V", "Paste clipboard text or forward image data"),
                new ShortcutRow("Middle-click", "Paste the host PRIMARY selection"),
                new ShortcutRow("Shift+PgUp / Shift+PgDn", "Scroll terminal history on the primary screen"),
                new ShortcutRow("Shift+F1..F12", "Forward the F-key to the agent"),
                new ShortcutRow("Ctrl+click", "Open a URL or navigate to a file"),
                new ShortcutRow("Right-click", "Open the terminal context menu"),
                new ShortcutRow("Double-click", "Select a word"),
                new ShortcutRow("Triple-click", "Select and publish a line"),
                new ShortcutRow("Mouse wheel", "Scroll history, or send arrows in an application"),
            }));
            groups.Add(new ShortcutGroup("Built-in · Interface", new[]
            {
                new ShortcutRow("?", "Open help from the map, or leave the help view"),
                new ShortcutRow("Escape", "Leave help and return to the backing pane or map"),
            }));
            return groups;
        }
    }
}
