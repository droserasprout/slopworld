using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // One row of portraits, shrunk, centred in a band the height of the terminal's title bar,
    // with a "+" on the end. Laid out that way in both views, so toggling a pane moves nothing.
    //
    // Or, with the sidebar layout on, a column down the left instead - same swap, a different
    // shape, and AgentSidebar owns that shape. This class stays the one place that takes the
    // bar's layout away from it and gives it back, because the two halves of that are a
    // prefix and a finalizer and neither of them is somewhere a second opinion can live.
    //
    // Nothing is redrawn by hand: the bar's own OnGUI is called with its cached scale and draw
    // locs pointed at the strip from a prefix, and put back from a finalizer.
    //
    // Over a terminal the call has to come from inside the window. UIRootOnGUI draws the map
    // interface, then every window's ExtraOnGUI, and only then every window's contents -
    // TerminalWindow fills the screen opaque, so anything from the first two is painted over.
    // Drawing after that fill also puts Mouse.IsOver in the right frame of reference. Hence
    // Draw(), and Suppressed to keep the map-layer call from drawing a buried second copy.
    //
    // A transpiler was tried first and this game's Mono rejected the rewritten wrapper with
    // InvalidProgramException at patch time, which in PatchAll takes the whole mod down.
    public static class ColonistBarStrip
    {
        public const float Shrink = 0.6f;

        // The name is inside the band, so the band is this much taller.
        const float LabelH = 16f;
        const float Pad = 4f;

        // GetPawnTextureRect draws PawnTextureSize (46x75) anchored to the bottom of a BaseSize
        // (48x48) cell, which would put heads off the top of the terminal window.
        static float Overhang(float s) =>
            (ColonistBarColonistDrawer.PawnTextureSize.y - ColonistBar.BaseSize.y) * s;

        static readonly FieldInfo DrawLocsField =
            AccessTools.Field(typeof(ColonistBar), "cachedDrawLocs");
        static readonly FieldInfo ScaleField =
            AccessTools.Field(typeof(ColonistBar), "cachedScale");

        // A reflection target that stops resolving must be loud once, not silently inert.
        static readonly bool Ready = Check();

        static bool Check()
        {
            if (DrawLocsField != null && ScaleField != null) return true;
            Log.Error("[SlopWorld] colonist bar fields moved; strip layout disabled");
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

        // The map-layer draw is pointless under a terminal and would register the bar's
        // reorderable groups twice a frame.
        public static bool Suppressed => Active && !Drawing;

        // Which shape the swap lays out. Read rather than stored: the setting can move
        // between two frames and both halves of the swap have to agree about which one they
        // are in, which they do by asking again.
        public static bool Vertical => SlopLayout.Sidebar;

        // Mirrors the private ColonistBar.Visible: the bar hides itself under 800x500 and
        // while the tile picker is up.
        public static bool BarShown =>
            UI.screenWidth >= 800 && UI.screenHeight >= 500 && !Find.TilePicker.Active;

        // Reserved before the row is centred, or the portraits shuffle sideways when it
        // appears.
        public static bool ShowAdd => BarShown && !Cutscene.Playing;

        // Set by Apply, the only thing that knows the fitted scale.
        public static Rect AddRect { get; private set; }

        static float RowH(float s) =>
            Overhang(s) + ColonistBar.BaseSize.y * s + LabelH;

        // Measured at the nominal scale, so a row that had to shrink leaves the band the same
        // height rather than making the chrome jump as agents come and go.
        public static float BarH => RowH(Shrink) + Pad * 2f;

        public static Rect Rect => new Rect(0f, 0f, UI.screenWidth, BarH);

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
            AddRect = Rect.zero;
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

            Rect add;
            float s = Vertical
                ? AgentSidebar.Place(entries, locs, count, plus, out add)
                : PlaceStrip(locs, count, plus, scale, out add);

            if (count > 0)
            {
                ScaleField.SetValue(bar, s);
                _applied = true;
            }

            if (plus) AddRect = add;
        }

        // One centred row, and the "+" on the end of it.
        static float PlaceStrip(List<Vector2> locs, int count, bool plus, float scale,
            out Rect add)
        {
            int cells = count + (plus ? 1 : 0);
            float s = FitScale(cells, scale > 0f ? scale : 1f);
            float w = ColonistBar.BaseSize.x * s;
            float h = ColonistBar.BaseSize.y * s;
            float gap = ColonistBar.BaseSpaceBetweenColonistsHorizontal * s;

            var strip = Rect;
            float x = strip.x + (strip.width - (cells * w + (cells - 1) * gap)) / 2f;
            // Centred rather than measured from the top, so a shrunk row sits in the middle.
            // The overhang goes back on: the loc is the cell's top and the head pokes above it.
            float y = strip.y + (strip.height - RowH(s)) / 2f + Overhang(s);

            for (int i = 0; i < count; i++)
            {
                locs[i] = new Vector2(x, y);
                x += w + gap;
            }

            add = plus ? new Rect(x, y, w, h) : Rect.zero;
            return s;
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

        // The strip is a single row by construction, where the bar's own layout would
        // have wrapped.
        static float FitScale(int cells, float scale)
        {
            float s = scale * Shrink;
            float need = cells * ColonistBar.BaseSize.x
                + (cells - 1) * ColonistBar.BaseSpaceBetweenColonistsHorizontal;
            // Both ends because the row is centred, and unconditionally: the map view has to
            // lay out the same pixels or toggling a terminal shuffles every portrait sideways.
            float room = UI.screenWidth - 40f - TerminalWindow.CornerW * 2f;
            if (need > 0f && need * s > room) s = room / need;
            return s;
        }
    }

    // A finalizer rather than a last-priority postfix, so an exception out of the bar cannot
    // strand the game at the strip's scale.
    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    public static class Patch_ColonistBarStripLayout
    {
        // The column's own chrome goes down around the bar's, from the same call, which is
        // what puts it over a pane as well as on the map: the panel and the project headings
        // under the portraits, the labels and their clicks over them. The strip layout has
        // neither and both are no-ops there.
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
            if (ColonistBarStrip.Blocked) return false;
            if (Event.current.type != EventType.MouseDown || Event.current.button != 0)
                return true;
            if (!Mouse.IsOver(rect)) return true; // not this portrait; fall through

            var session = AgentColony.Current?.SessionOf(colonist);
            if (session == null) return true;

            var info = SessionHub.Instance.Get(session);
            if (info == null) return true;

            // A down agent has no pane to show; the click starts it back up.
            if (info.Gone) SessionHub.Instance.Start(session);
            else if (session != TerminalWindow.CurrentName) TerminalWindow.Open(session);

            Event.current.Use();
            return false;
        }
    }
}
