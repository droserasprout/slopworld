using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The entry point that takes the bar's layout away from it and gives it back, redirecting
    // to AgentSidebar for the column layout. The prefix and finalizer are the only things that
    // touch the bar's cached layout, so nothing else has to know about the swap.
    //
    // Over a terminal the call has to come from inside the window. UIRootOnGUI draws the map
    // interface, then every window's ExtraOnGUI, and only then every window's contents -
    // TerminalWindow fills the screen opaque, so anything from the first two is painted over.
    // Drawing after that fill also puts Mouse.IsOver in the right frame of reference. Hence
    // Draw(), and Suppressed to keep the map-layer call from drawing a buried second copy.
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

        // The strip draws from the terminal's contents, which run before anything stacked above
        // it, so with a dialog up the bar would still be answering clicks underneath. It keeps
        // drawing; it stops listening. On the map layer this never arises, HandleEventsHighPriority
        // having already Used the event.
        public static bool Blocked { get; private set; }

        // What the strip's own handlers ask instead of Blocked, read wherever the column
        // decides whether to answer a click. It carried an exception for a while - the options
        // menu, which opened *inside* the chrome and so was the one window the column was kept
        // alive under - and the exception bought nothing: a window under an absorbing one is
        // never called for a MouseDown, so the column kept its hover and lost every press
        // regardless (see gotchas.md). The options menu is a content view now, drawn *by* this
        // window rather than by one over it, so this is the plain answer again.
        public static bool Interactive => !Blocked;

        // The mouse is over a rect on the strip, bypassing `Mouse.IsOver`'s input-blocked gate.
        // `Mouse.IsOver` calls `IsInputBlockedNow` which returns true when the current window
        // does not get input, which is the terminal's state whenever anything is stacked over
        // the pane. The strip draws in screen coordinates from inside that window, so a direct
        // `Contains` is the right answer and is what every hover and press path here uses.
        public static bool MouseOver(Rect r) => r.Contains(Event.current.mousePosition);

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

    // Selecting off the bar does not happen inside ColonistBarOnGUI: the Selector asks
    // TryGetEntryAt while the *map* handles the click, long after the finalizer above has put
    // the vanilla layout back - so the click was tested against where the portraits would be
    // without this mod. The two layouts overlap for part of the row, which is why it read as
    // some colonists selecting and some not.
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

            // A down agent has no pane to show; the click starts it back up.
            if (info.Gone) SessionHub.Instance.Start(session);
            else if (session != TerminalWindow.CurrentName) TerminalWindow.Open(session);

            Event.current.Use();
            return false;
        }
    }
}
