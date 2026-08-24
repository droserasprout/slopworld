using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Redirect colonist-bar layout to AgentSidebar. Over a terminal, draw from the window after
    // its opaque fill; suppress the map-layer copy to avoid drawing a buried duplicate.
    public static class ColonistBarStrip
    {
        static readonly FieldInfo DrawLocsField =
            AccessTools.Field(typeof(ColonistBar), "cachedDrawLocs");
        static readonly FieldInfo ScaleField =
            AccessTools.Field(typeof(ColonistBar), "cachedScale");

        // A reflection target that stops resolving must be loud once, not silently inert.
        static readonly bool Ready = Check();

        static bool Check()
        {
            if (DrawLocsField != null && ScaleField != null) return true;
            Log.Error("[SlopWorld] colonist bar fields moved; sidebar layout disabled");
            return false;
        }

        static readonly List<Vector2> Saved = new List<Vector2>();

        // What Restore has to undo.
        static bool _applied;
        static float _savedScale;

        // Read by the hit-test patch, which must not restore a layout somebody else is still
        // drawing with.
        public static bool Applied => _applied;

        public static bool Active =>
            Ready && Find.WindowStack?.WindowOfType<TerminalWindow>() != null;

        // True only for the length of the terminal's own call to the bar.
        public static bool Drawing { get; private set; }

        // A stacked dialog leaves the bar visible below it but must block its input.
        public static bool Blocked { get; private set; }

        // Internal handlers use this gate to match the current input state.
        public static bool Interactive => !Blocked;

        // Use direct screen-rect hit testing because Mouse.IsOver rejects input from an
        // absorbing window.
        public static bool MouseOver(Rect r) => r.Contains(Event.current.mousePosition);

        // Hover requires interactive chrome and no float menu; map-layer menus consume presses
        // without drawing hover, so the underlying list must not highlight.
        public static bool Hover(Rect r) =>
            Interactive && Find.WindowStack?.FloatMenu == null && MouseOver(r);

        // SlopMenu is used for the sidebar's own context menus. Keep it out of Hover: the
        // status bar shares that predicate for presses, and its doors intentionally replace
        // an open SlopMenu when clicked.
        public static bool SidebarHover(Rect r) =>
            Find.WindowStack?.WindowOfType<SlopMenu>() == null && Hover(r);

        // The map-layer draw is pointless under a terminal and would register the bar's
        // reorderable groups twice a frame.
        public static bool Suppressed => Active && !Drawing;

        // Mirrors the private ColonistBar.Visible: the bar hides itself under 800x500 and
        // while the tile picker is up.
        public static bool BarShown =>
            UI.screenWidth >= 800 && UI.screenHeight >= 500 && !Find.TilePicker.Active;

        // Whether the column's add strip has the foot of the panel, which is the same
        // question as whether the panel is on screen at all. The layout is told so it can
        // keep the room off the rows; the button itself is drawn and answered by
        // AgentSidebar, in every view rather than only this one.
        public static bool ShowAdd => BarShown && !Cutscene.Playing;

        // From TerminalWindow.DoWindowContents, after the background fill.
        public static void Draw(bool interactive)
        {
            if (!Ready || Drawing) return;
            if (Event.current.type == EventType.Layout) return;
            var bar = Find.ColonistBar;
            if (bar == null) return;

            Drawing = true;
            Blocked = !interactive;
            try { bar.ColonistBarOnGUI(); }
            finally { Drawing = false; Blocked = false; }
        }

        public static void Apply()
        {
            if (!Ready || _applied || Suppressed || Cutscene.Playing) return;
            var bar = Find.ColonistBar;
            if (bar == null) return;

            // Entries first, and held: it recaches both fields below from scratch when the
            // layout is dirty, from inside ColonistBarOnGUI, undoing everything this sets.
            var entries = bar.Entries;
            var locs = DrawLocsField.GetValue(bar) as List<Vector2>;
            int count = entries.Count == 0 || locs == null ? 0 : locs.Count;

            float scale = (float)ScaleField.GetValue(bar);
            bool plus = ShowAdd;
            int cells = count + (plus ? 1 : 0);
            if (cells == 0) return;

            // Saved before either layout writes into the list it was handed.
            if (count > 0)
            {
                Saved.Clear();
                Saved.AddRange(locs);
                _savedScale = scale;
            }

            float s = AgentSidebar.Place(entries, locs, count, plus);

            if (count > 0)
            {
                ScaleField.SetValue(bar, s);
                _applied = true;
            }
        }

        public static void Restore()
        {
            if (!_applied) return;
            _applied = false;

            var bar = Find.ColonistBar;
            if (bar == null) return;

            // Restoring beats marking it dirty, which would recache everything every frame.
            ScaleField.SetValue(bar, _savedScale);
            if (DrawLocsField.GetValue(bar) is List<Vector2> locs)
            {
                locs.Clear();
                locs.AddRange(Saved);
            }
        }
    }

    // A finalizer rather than a last-priority postfix, so an exception out of the bar cannot
    // strand the game at the strip's scale.
    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    public static class Patch_ColonistBarStripLayout
    {
        // The column's own chrome goes down around the bar's, from the same call, which is
        // what puts it over a pane as well as on the map: the panel and the project headings
        // under the portraits, the labels and their clicks over them.
        static void Prefix()
        {
            ColonistBarStrip.Apply();
            AgentSidebar.DrawBack();
        }

        static void Postfix() => AgentSidebar.DrawFront();

        // A postfix does not run when the original throws, and the flag the front pass clears
        // is what stops every pawn label on the map being declined. So the finalizer clears
        // it too, the same reason the layout is put back from here.
        static void Finalizer()
        {
            AgentSidebar.EndDraw();
            ColonistBarStrip.Restore();
        }
    }

    // Selector.TryGetEntryAt runs after ColonistBarOnGUI restores vanilla layout, so apply the
    // sidebar layout around hit-testing as well.
    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.TryGetEntryAt))]
    public static class Patch_StripHitTest
    {
        // Only the outermost call owns the swap: the bar asks this of itself from inside its
        // own OnGUI, and restoring there would undo the layout being drawn.
        static void Prefix(out bool __state)
        {
            __state = !ColonistBarStrip.Applied;
            if (__state) ColonistBarStrip.Apply();
        }

        static void Finalizer(bool __state)
        {
            if (__state) ColonistBarStrip.Restore();
        }
    }

    // Vanilla's double-click jump and its right-click swallow stay out of the way
    // underneath; only the strip's own clicks are taken.
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), "HandleClicks")]
    public static class Patch_BarClickSwitchesTerminal
    {
        static bool Prefix(Rect rect, Pawn colonist)
        {
            if (!ColonistBarStrip.Drawing) return true;
            // Something is stacked over the pane; the strip is scenery this frame.
            if (!ColonistBarStrip.Interactive) return false;
            if (Event.current.type != EventType.MouseDown || Event.current.button != 0)
                return true;
            if (!ColonistBarStrip.MouseOver(rect)) return true; // not this portrait; fall through

            var session = AgentColony.Current?.SessionOf(colonist);
            if (session == null) return true;

            var info = SessionHub.Instance.Get(session);
            if (info == null) return true;

            // The current session follows the bar click.
            SessionSelectable.Current = session;

            if (session != TerminalWindow.CurrentName) TerminalWindow.Open(session);

            Event.current.Use();
            return false;
        }
    }
}
