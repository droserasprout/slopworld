using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// While a terminal is open, the colonist bar leaves the top of the screen and
    /// sits in the pane's title bar, smaller: the same bar, the same portraits, one
    /// click from the session you're typing at. Nothing is redrawn by hand - the
    /// bar's own OnGUI is called with its cached scale and draw locs pointed at
    /// the strip.
    ///
    /// The strip is *in* the header rather than under it. A row of portraits with a
    /// title bar above it is two bands of chrome over the pane; the header grown to
    /// <see cref="BarH"/> - a whole row, name included, with a pad above and below -
    /// is one.
    ///
    /// It has to be called from inside the window, which is the whole trick:
    ///
    ///   UIRoot_Play.UIRootOnGUI  ->  MapInterfaceOnGUI_BeforeMainTabs (the bar)
    ///                            ->  WindowStackOnGUI: every ExtraOnGUI, THEN
    ///                                every WindowOnGUI
    ///
    /// The bar's own draw and every window's ExtraOnGUI both run before any window
    /// contents, and TerminalWindow fills the screen with an opaque background, so
    /// anything drawn from either place is painted over. Only a draw inside
    /// DoWindowContents, after that fill, can be seen. Drawing there also puts
    /// Mouse.IsOver in the right frame of reference: the terminal is then the
    /// currently drawn window, so clicks in the strip register instead of counting
    /// as obscured.
    ///
    /// A transpiler was tried first and this game's Mono rejected the rewritten
    /// wrapper with InvalidProgramException at patch time, which in PatchAll takes
    /// the entire mod down with it - so this is plain prefix/postfix work.
    ///
    /// A prefix on ColonistBarColonistDrawer.HandleClicks turns a click on a
    /// colonist into a terminal switch instead of a camera jump.
    /// </summary>
    public static class ColonistBarOverlay
    {
        /// How much smaller than wherever the bar already was. Small enough to read
        /// as a strip and not a second bar, large enough to still be a portrait.
        public const float Shrink = 0.6f;

        /// The pawn name the bar draws under each portrait (GameFont.Tiny). It is
        /// inside the title bar with everything else, so the bar is this much taller
        /// rather than the name hanging onto the pane below it.
        const float LabelH = 16f;
        const float Pad = 4f;

        /// <summary>How far a portrait pokes out the top of the cell it is laid out
        /// in. GetPawnTextureRect draws PawnTextureSize (46x75) anchored to the
        /// bottom of a BaseSize (48x48) cell, which is what lets heads clear the
        /// top of the screen in vanilla. Here it would put them off the top of the
        /// window.</summary>
        static float Overhang(float s) =>
            (ColonistBarColonistDrawer.PawnTextureSize.y - ColonistBar.BaseSize.y) * s;

        static readonly FieldInfo DrawLocsField =
            AccessTools.Field(typeof(ColonistBar), "cachedDrawLocs");
        static readonly FieldInfo ScaleField =
            AccessTools.Field(typeof(ColonistBar), "cachedScale");

        /// A reflection target that stops resolving must be loud once, not silently
        /// inert: the feature would otherwise look like it was never built.
        static readonly bool Ready = Check();

        static bool Check()
        {
            if (DrawLocsField != null && ScaleField != null) return true;
            Log.Error("[SlopWorld] colonist bar fields moved; terminal strip disabled");
            return false;
        }

        /// Scratch for the positions the strip borrows and hands back.
        static readonly List<Vector2> Saved = new List<Vector2>();

        public static bool Active =>
            Ready && Find.WindowStack?.WindowOfType<TerminalWindow>() != null;

        /// True only for the length of the strip's own call to the bar.
        public static bool Drawing { get; private set; }

        /// The map-layer draw is pointless while the terminal covers it, and would
        /// leave the bar's reorderable groups registered twice a frame. Read by the
        /// strip-UI gate and by the "+" slot's postfix.
        public static bool Suppressed => Active && !Drawing;

        /// <summary>A row's own extent, top of the head to the bottom of the name:
        /// the overhang, the cell, the label. What has to fit inside the title bar,
        /// and what gets centred in it.</summary>
        static float RowH(float s) =>
            Overhang(s) + ColonistBar.BaseSize.y * s + LabelH;

        /// <summary>How tall the title bar has to be to hold a whole row - portrait
        /// and name both - with a pad above and below. Measured at the nominal scale,
        /// so a row that had to shrink to fit leaves the bar the same height rather
        /// than making the chrome jump about as agents come and go.</summary>
        public static float BarH => RowH(Shrink) + Pad * 2f;

        /// <summary>The strip the bar is rerouted into while the terminal is up, and
        /// the room the terminal leaves for it - which is the title bar itself, since
        /// the whole row lives inside it now. Zero-sized otherwise.</summary>
        public static Rect Rect =>
            Active ? new Rect(0f, 0f, UI.screenWidth, BarH) : Rect.zero;

        /// <summary>Draws the bar into the strip. Called from TerminalWindow's
        /// contents, after the background fill, on every event but Layout - the bar
        /// handles its own clicks and returns early on Layout anyway.</summary>
        public static void Draw()
        {
            if (!Ready || Drawing) return;
            if (Event.current.type == EventType.Layout) return;
            var bar = Find.ColonistBar;
            if (bar == null) return;

            // Touch Entries first: it recaches both fields below from scratch when
            // the layout is dirty, and does so from inside ColonistBarOnGUI, which
            // would undo everything this method sets. Forcing it now means the
            // recache inside is a no-op.
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
            // Centred in the bar rather than measured down from its top, so a row
            // that had to shrink sits in the middle of the chrome instead of riding
            // its ceiling. The overhang is added back because the loc is the cell's
            // top and the head pokes out above it - what is being centred is the
            // whole row, name included.
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
                // Put the bar back where it believes it is. Restoring beats marking
                // it dirty: a dirty flag would recache the whole thing every frame
                // the terminal is open.
                ScaleField.SetValue(bar, scale);
                locs.Clear();
                locs.AddRange(Saved);
            }
        }

        /// <summary>Shrink, then shrink further if that many portraits still don't
        /// fit one row - the strip is a single row by construction, where the bar's
        /// own layout would have wrapped.</summary>
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

    /// <summary>
    /// A left click on a portrait in the strip points the terminal at that agent
    /// instead of jumping the camera to it. Vanilla's double-click jump and its
    /// right-click swallow stay out of the way underneath; only the strip's own
    /// clicks are taken.
    /// </summary>
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

            // A down agent has no pane to show; the click starts it back up, and
            // the terminal follows once the process is up and the pawn is standing.
            if (info.Gone) SessionHub.Instance.Start(session);
            else if (session != TerminalWindow.CurrentName) TerminalWindow.Open(session);

            Event.current.Use();
            return false;
        }
    }
}
