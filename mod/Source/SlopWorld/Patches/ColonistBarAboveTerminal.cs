using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // While a terminal is open the colonist bar leaves the top of the screen and sits
    // in the pane's title bar, smaller. Nothing is redrawn by hand: the bar's own
    // OnGUI is called with its cached scale and draw locs pointed at the strip, which
    // is *in* the header rather than under it - a row of portraits below a title bar
    // is two bands of chrome over the pane where one grown to BarH is one.
    //
    // It has to be called from inside the window, which is the whole trick:
    // UIRootOnGUI draws the map interface (the bar), then every window's ExtraOnGUI,
    // and only then every window's contents. TerminalWindow fills the screen opaque,
    // so anything drawn from the first two is painted over. Drawing after that fill
    // also puts Mouse.IsOver in the right frame of reference, so clicks in the strip
    // register instead of counting as obscured.
    //
    // A transpiler was tried first and this game's Mono rejected the rewritten
    // wrapper with InvalidProgramException at patch time, which in PatchAll takes the
    // whole mod down.
    public static class ColonistBarOverlay
    {
        // Small enough to read as a strip and not a second bar, large enough to still be
        // a portrait.
        public const float Shrink = 0.6f;

        // The name is inside the title bar with everything else, so the bar is this much
        // taller rather than the name hanging onto the pane below it.
        const float LabelH = 16f;
        const float Pad = 4f;

        // GetPawnTextureRect draws PawnTextureSize (46x75) anchored to the bottom of a
        // BaseSize (48x48) cell, which lets heads clear the top of the screen in vanilla
        // and would put them off the top of the window here.
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
            Log.Error("[SlopWorld] colonist bar fields moved; terminal strip disabled");
            return false;
        }

        static readonly List<Vector2> Saved = new List<Vector2>();

        public static bool Active =>
            Ready && Find.WindowStack?.WindowOfType<TerminalWindow>() != null;

        // True only for the length of the strip's own call to the bar.
        public static bool Drawing { get; private set; }

        // The map-layer draw is pointless while the terminal covers it, and would leave
        // the bar's reorderable groups registered twice a frame.
        public static bool Suppressed => Active && !Drawing;

        // What has to fit inside the title bar, and what gets centred in it.
        static float RowH(float s) =>
            Overhang(s) + ColonistBar.BaseSize.y * s + LabelH;

        // Measured at the nominal scale, so a row that had to shrink leaves the bar the
        // same height rather than making the chrome jump about as agents come and go.
        public static float BarH => RowH(Shrink) + Pad * 2f;

        // The room the terminal leaves for it is the title bar itself, since the whole
        // row lives inside it now.
        public static Rect Rect =>
            Active ? new Rect(0f, 0f, UI.screenWidth, BarH) : Rect.zero;

        // On every event but Layout - the bar handles its own clicks and returns early on
        // Layout anyway.
        public static void Draw()
        {
            if (!Ready || Drawing) return;
            if (Event.current.type == EventType.Layout) return;
            var bar = Find.ColonistBar;
            if (bar == null) return;

            // Touch Entries first: it recaches both fields below from scratch when the layout
            // is dirty, and does so from inside ColonistBarOnGUI, which would undo everything
            // this method sets.
            if (bar.Entries.Count == 0) return;

            var locs = DrawLocsField.GetValue(bar) as List<Vector2>;
            if (locs == null || locs.Count == 0) return;

            float scale = (float)ScaleField.GetValue(bar);
            Saved.Clear();
            Saved.AddRange(locs);

            float s = FitScale(locs.Count, scale);
            ScaleField.SetValue(bar, s);

            var strip = Rect;
            float w = ColonistBar.BaseSize.x * s;
            float gap = ColonistBar.BaseSpaceBetweenColonistsHorizontal * s;
            float x = strip.x + (strip.width - (locs.Count * w + (locs.Count - 1) * gap)) / 2f;
            // Centred in the bar rather than measured down from its top, so a row that had to
            // shrink sits in the middle of the chrome. The overhang is added back because the
            // loc is the cell's top and the head pokes out above it.
            float y = strip.y + (strip.height - RowH(s)) / 2f + Overhang(s);

            for (int i = 0; i < locs.Count; i++)
            {
                locs[i] = new Vector2(x, y);
                x += w + gap;
            }

            Drawing = true;
            try
            {
                bar.ColonistBarOnGUI();
            }
            finally
            {
                Drawing = false;
                // Restoring beats marking it dirty: a dirty flag would recache the whole thing
                // every frame the terminal is open.
                ScaleField.SetValue(bar, scale);
                locs.Clear();
                locs.AddRange(Saved);
            }
        }

        // The strip is a single row by construction, where the bar's own layout would
        // have wrapped.
        static float FitScale(int count, float scale)
        {
            float s = scale * Shrink;
            float need = count * ColonistBar.BaseSize.x
                + (count - 1) * ColonistBar.BaseSpaceBetweenColonistsHorizontal;
            float room = UI.screenWidth - 40f;
            if (need > 0f && need * s > room) s = room / need;
            return s;
        }
    }

    // Vanilla's double-click jump and its right-click swallow stay out of the way
    // underneath; only the strip's own clicks are taken.
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), "HandleClicks")]
    public static class Patch_BarClickSwitchesTerminal
    {
        static bool Prefix(Rect rect, Pawn colonist)
        {
            if (!ColonistBarOverlay.Drawing) return true;
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
