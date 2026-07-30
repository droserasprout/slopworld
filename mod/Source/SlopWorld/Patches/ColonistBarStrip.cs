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

        // Mirrors the private ColonistBar.Visible: the bar hides itself under 800x500 and
        // while the tile picker is up.
        static bool BarShown =>
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

            // Entries first: it recaches both fields below from scratch when the layout is
            // dirty, from inside ColonistBarOnGUI, undoing everything this sets.
            int entries = bar.Entries.Count;
            var locs = DrawLocsField.GetValue(bar) as List<Vector2>;
            int count = entries == 0 || locs == null ? 0 : locs.Count;

            float scale = (float)ScaleField.GetValue(bar);
            bool plus = ShowAdd;
            int cells = count + (plus ? 1 : 0);
            if (cells == 0) return;

            float s = FitScale(cells, scale > 0f ? scale : 1f);
            float w = ColonistBar.BaseSize.x * s;
            float h = ColonistBar.BaseSize.y * s;
            float gap = ColonistBar.BaseSpaceBetweenColonistsHorizontal * s;

            var strip = Rect;
            float x = strip.x + (strip.width - (cells * w + (cells - 1) * gap)) / 2f;
            // Centred rather than measured from the top, so a shrunk row sits in the middle.
            // The overhang goes back on: the loc is the cell's top and the head pokes above it.
            float y = strip.y + (strip.height - RowH(s)) / 2f + Overhang(s);

            if (count > 0)
            {
                Saved.Clear();
                Saved.AddRange(locs);
                _savedScale = scale;
                ScaleField.SetValue(bar, s);
                for (int i = 0; i < count; i++)
                {
                    locs[i] = new Vector2(x, y);
                    x += w + gap;
                }
                _applied = true;
            }

            if (plus) AddRect = new Rect(x, y, w, h);
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
        static void Prefix() => ColonistBarStrip.Apply();
        static void Finalizer() => ColonistBarStrip.Restore();
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
