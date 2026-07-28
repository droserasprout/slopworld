using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The colonist bar is a strip: one row of portraits, shrunk, centred in a band the
    // height of the terminal's title bar, with a "+" slot on the end. It is laid out
    // that way in both views, so opening or closing a pane moves nothing - the strip is
    // the same pixels whether the map or a terminal is underneath it.
    //
    // Nothing is redrawn by hand. The bar's own OnGUI is called with its cached scale
    // and draw locs pointed at the strip, from a prefix, and put back from a finalizer.
    // One code path, so the two views cannot drift.
    //
    // Over a terminal the call has to come from inside the window, which is the whole
    // trick: UIRootOnGUI draws the map interface (the bar), then every window's
    // ExtraOnGUI, and only then every window's contents. TerminalWindow fills the screen
    // opaque, so anything drawn from the first two is painted over. Drawing after that
    // fill also puts Mouse.IsOver in the right frame of reference, so clicks in the
    // strip register instead of counting as obscured. Hence Draw(), and Suppressed to
    // keep the map-layer call from drawing a second, buried copy.
    //
    // A transpiler was tried first and this game's Mono rejected the rewritten wrapper
    // with InvalidProgramException at patch time, which in PatchAll takes the whole mod
    // down.
    public static class ColonistBarStrip
    {
        // Small enough to read as a strip and not a second bar, large enough to still be
        // a portrait.
        public const float Shrink = 0.6f;

        // The name is inside the band with everything else, so the band is this much
        // taller rather than the name hanging onto whatever is below it.
        const float LabelH = 16f;
        const float Pad = 4f;

        // GetPawnTextureRect draws PawnTextureSize (46x75) anchored to the bottom of a
        // BaseSize (48x48) cell, which lets heads clear the top of the screen in vanilla
        // and would put them off the top of the terminal window here.
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

        public static bool Active =>
            Ready && Find.WindowStack?.WindowOfType<TerminalWindow>() != null;

        // True only for the length of the terminal's own call to the bar.
        public static bool Drawing { get; private set; }

        // The strip is drawn from the terminal's contents, which run before anything
        // stacked above it - so with a dialog up (the one "+" just opened, say) the bar
        // would still be answering clicks underneath it. It keeps drawing; it stops
        // listening. On the map layer this never arises: HandleEventsHighPriority has
        // already Used the event by the time the map interface draws.
        public static bool Blocked { get; private set; }

        // The map-layer draw is pointless while the terminal covers it, and would leave
        // the bar's reorderable groups registered twice a frame.
        public static bool Suppressed => Active && !Drawing;

        // Mirrors ColonistBar.Visible, which is private: the bar hides itself under
        // 800x500 and while the tile picker is up.
        static bool BarShown =>
            UI.screenWidth >= 800 && UI.screenHeight >= 500 && !Find.TilePicker.Active;

        // The "+" is part of the row, so it is reserved before the row is centred - the
        // portraits would otherwise shuffle sideways the moment it appeared.
        public static bool ShowAdd => BarShown && !Cutscene.Playing;

        // Where the add button goes, in the same cell geometry as a portrait. Set by
        // Apply, which is the only thing that knows the fitted scale.
        public static Rect AddRect { get; private set; }

        // What has to fit inside the band, and what gets centred in it.
        static float RowH(float s) =>
            Overhang(s) + ColonistBar.BaseSize.y * s + LabelH;

        // Measured at the nominal scale, so a row that had to shrink leaves the band the
        // same height rather than making the chrome jump about as agents come and go.
        public static float BarH => RowH(Shrink) + Pad * 2f;

        // The top of the screen in either view; over a terminal it is the title bar,
        // since the whole row lives inside it now.
        public static Rect Rect => new Rect(0f, 0f, UI.screenWidth, BarH);

        // Called from TerminalWindow.DoWindowContents, after the background fill.
        // On every event but Layout - the bar handles its own clicks and returns early on
        // Layout anyway.
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

        // Prefixed onto the bar's OnGUI, so the map-layer call and the terminal's own
        // land on identical geometry.
        public static void Apply()
        {
            AddRect = Rect.zero;
            if (!Ready || _applied || Suppressed || Cutscene.Playing) return;
            var bar = Find.ColonistBar;
            if (bar == null) return;

            // Touch Entries first: it recaches both fields below from scratch when the layout
            // is dirty, and does so from inside ColonistBarOnGUI, which would undo everything
            // this method sets.
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
            // Centred in the band rather than measured down from its top, so a row that had
            // to shrink sits in the middle of the chrome. The overhang is added back because
            // the loc is the cell's top and the head pokes out above it.
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

            // Restoring beats marking it dirty: a dirty flag would recache the whole thing
            // every frame.
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
            float room = UI.screenWidth - 40f;
            if (need > 0f && need * s > room) s = room / need;
            return s;
        }
    }

    // Apply before the bar lays anything out, put it back after everything that reads
    // the layout has run. A finalizer rather than a last-priority postfix, so an
    // exception out of the bar cannot strand the game at the strip's scale.
    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    public static class Patch_ColonistBarStripLayout
    {
        static void Prefix() => ColonistBarStrip.Apply();
        static void Finalizer() => ColonistBarStrip.Restore();
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
