using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A map-only reference for both live key bindings and shortcuts that deliberately do not
    // go through KeyBindingDef. It is a window so it can sit over the map, but it never opens
    // over TerminalWindow: `?` remains a character the agent can receive there.
    public sealed class ShortcutHelpWindow : SlopWindow
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

        const float Width = 700f;
        const float MaxHeight = 760f;
        const float MinHeight = 360f;
        const float KeyColumnMin = 104f;
        const float KeyColumnMax = 160f;
        const float PairGap = SlopWidgets.GapM;
        const float GroupGap = SlopWidgets.GapXS;

        readonly SmoothScroll _scroll = new SmoothScroll();

        ShortcutHelpWindow()
        {
            closeOnClickedOutside = true;
            forcePause = false;
            preventCameraMotion = true;
            layer = WindowLayer.Super;
        }

        public static void Toggle()
        {
            var open = Find.WindowStack?.WindowOfType<ShortcutHelpWindow>();
            if (open != null)
            {
                open.Close();
                return;
            }

            if (!SlopProfile.Ok || Cutscene.Playing ||
                Current.ProgramState != ProgramState.Playing ||
                Find.CurrentMap == null ||
                Find.WindowStack?.WindowOfType<TerminalWindow>() != null)
                return;

            Find.WindowStack.Add(new ShortcutHelpWindow());
        }

        // GameComponentOnGUI sees the map event before WindowStack and before a focused
        // vanilla control. The terminal check is repeated here as a guard for future callers.
        public static bool HandleMapKey(Event e)
        {
            if (e == null || e.type != EventType.KeyDown ||
                !SlopProfile.Ok || Cutscene.Playing ||
                Current.ProgramState != ProgramState.Playing ||
                Find.CurrentMap == null ||
                Find.WindowStack?.WindowOfType<TerminalWindow>() != null || !IsHelpKey(e))
                return false;

            Toggle();
            e.Use();
            return true;
        }

        static bool IsHelpKey(Event e)
        {
            if (e.control || e.alt || e.command) return false;
            // The character catches non-US layouts; Slash+Shift covers Unity players that
            // report the physical key but lose the resolved question-mark character.
            return e.character == '?' || (e.keyCode == KeyCode.Slash && e.shift);
        }

        public override Vector2 InitialSize
        {
            get
            {
                float width = Mathf.Min(Width, Mathf.Max(360f, UI.screenWidth - 32f));
                float height = Mathf.Min(MaxHeight, Mathf.Max(MinHeight, UI.screenHeight - 32f));
                return new Vector2(width, height);
            }
        }

        protected override void SetInitialSizeAndPosition()
        {
            var size = InitialSize;
            windowRect = new Rect((UI.screenWidth - size.x) / 2f,
                (UI.screenHeight - size.y) / 2f, size.x, size.y);
        }

        protected override void DoBody(Rect rect)
        {
            var groups = Groups();
            var list = rect;
            float keyWidth = KeyWidth(groups, list.width);
            float total = ContentHeight(groups);
            var view = new Rect(0f, 0f,
                Mathf.Max(1f, list.width - SlopWidgets.ScrollbarW), Mathf.Max(total, list.height));

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
                        widest = Mathf.Max(widest, SlopWidgets.Wide(row.Key) +
                            SlopWidgets.FieldPadX * 2f);
            }
            float pairWidth = Mathf.Max(1f, (available - PairGap) / 2f);
            return Mathf.Clamp(widest, KeyColumnMin,
                Mathf.Min(KeyColumnMax, pairWidth * 0.55f));
        }

        static float ContentHeight(List<ShortcutGroup> groups)
        {
            float total = 0f;
            foreach (var group in groups)
                total += SlopWidgets.TinyRowH +
                    ((group.Rows.Count + 1) / 2) * SlopWidgets.PaletteRowH + GroupGap;
            return total;
        }

        static float DrawGroup(ShortcutGroup group, float y, float width, float keyWidth)
        {
            var heading = new Rect(0f, y, width, SlopWidgets.TinyRowH);
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Tiny;
                GUI.color = SlopWidgets.Faint;
                SlopWidgets.RowLabel(heading, group.Title.ToUpperInvariant());
                Slab.Hairline(new Rect(0f, heading.yMax - 1f, width, 1f), SlopWidgets.Edge);
            }
            y += SlopWidgets.TinyRowH;

            float pairWidth = (width - PairGap) / 2f;
            int rowCount = (group.Rows.Count + 1) / 2;
            for (int i = 0; i < rowCount; i++)
            {
                DrawPair(group.Rows[i * 2], new Rect(0f, y, pairWidth,
                    SlopWidgets.PaletteRowH), keyWidth);
                if (i * 2 + 1 < group.Rows.Count)
                    DrawPair(group.Rows[i * 2 + 1], new Rect(pairWidth + PairGap, y,
                        pairWidth, SlopWidgets.PaletteRowH), keyWidth);
                y += SlopWidgets.PaletteRowH;
            }

            return y + GroupGap;
        }

        static void DrawPair(ShortcutRow shortcut, Rect row, float keyWidth)
        {
            if (Mouse.IsOver(row)) Slab.Fill(row, SlopWidgets.RowBg);

            var key = new Rect(row.x, row.y + 2f, keyWidth, row.height - 4f);
            Slab.Box(key, SlopWidgets.Well, SlopWidgets.Edge);
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                GUI.color = SlopWidgets.Lead;
                SlopWidgets.RowLabel(key.ContractedBy(SlopWidgets.FieldPadX, 0f), shortcut.Key);
                GUI.color = SlopWidgets.Name;
                var action = new Rect(key.xMax + SlopWidgets.GapS, row.y,
                    row.xMax - key.xMax - SlopWidgets.GapS, row.height);
                SlopWidgets.RowLabel(action, shortcut.Action);
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
            }));
            groups.Add(new ShortcutGroup("Built-in · Terminal", new[]
            {
                new ShortcutRow("Escape", "Forward to the agent"),
                new ShortcutRow("Shift+Escape", "Close the terminal"),
                new ShortcutRow("Alt+1..9, Alt+0", "Select an agent by sidebar position"),
                new ShortcutRow("Shift+Enter", "Send a newline without submitting"),
                new ShortcutRow("Ctrl+C", "Copy selected text; otherwise send SIGINT"),
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
                new ShortcutRow("?", "Show or hide this window on the map"),
            }));
            return groups;
        }
    }
}
