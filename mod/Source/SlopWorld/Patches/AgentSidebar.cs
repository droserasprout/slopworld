using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    [Flags]
    public enum AgentStatusFilter
    {
        All = 0,
        Active = 1,
        Idle = 2,
        Down = 4,
    }

    public static partial class AgentSidebar
    {
        public const float MinWidth = 150f;
        public const float MaxWidth = 460f;

        public static float Width => Mathf.Clamp(Settings.SidebarWidth, MinWidth,
            Mathf.Max(MinWidth, Mathf.Min(MaxWidth, UI.screenWidth * 0.4f)));

        // Size square portraits from the text row, then clamp them so the cached texture is
        // neither upsampled nor reduced to a thumbnail.
        static float Nominal => Mathf.Clamp(
            Mathf.Min(Patch_SidebarPortraitDraw.FaceForHeight(TextH), Width * WidthShare)
                / ColonistBarColonistDrawer.PawnTextureSize.y,
            Floor, Ceiling);

        const float WidthShare = 0.35f;
        const float Floor = 0.3f;
        const float Ceiling = 1f;

        static float HeadH => SlopWidgets.TinyRowH;
        static float AddH => TopBar.H;

        const float AddIcon = SlopWidgets.IconW;
        const float Pad = SlopWidgets.GapS;
        // Portraits touch the screen edge; CellX remains the inset for labels and chrome.
        const float PortraitX = 0f;
        const float CellX = SlopWidgets.GapS;
        const float TextGap = SlopWidgets.GapS;

        // The shared tabs/filter row is followed by an optional right-aligned view-action row;
        // `TabH` includes both when present.
        static float TabRowH => TopBar.H;
        public static float TabH => TabRowH + (HasActions ? TabRowH : 0f);
        const float TabIcon = 20f;

        const float RowGap = SlopWidgets.GapXS;

        static float GhostH => NameH + 2f;
        // Child workers get a single compact line and a small robot mark instead of a portrait.
        static float WorkerH => SlopWidgets.LineHOf(GameFont.Tiny) + 2f;

        static float NameH => SlopWidgets.LineHOf(GameFont.Small);
        static float SubH => SlopWidgets.TinyH;

        // Compact rows keep the name and summary, but do not reserve the unused third line.
        static float TextH => NameH + SubH * (CompactView ? 1f : 2f);

        public static bool CompactView => true;

        const float BellW = 13f;

        // The state badge is a share of the portrait rather than a fixed size: the column
        // shrinks to fit and a marker that did not would swallow a small face. Keep its own
        // smaller floor so the circle does not dominate a compact portrait.
        const float BadgeShare = 0.18f;
        const float BadgeMin = 6f;
        const float BadgeInset = 1f;
        const float BadgeRing = 1.5f;
        const float BadgeAlpha = 0.8f;

        const float GhostMarkW = 12f;

        const float ArrowW = 12f;
        const float GripW = 5f;


        // The colonist bar draws and hit-tests the same location table.
        static readonly Vector2 Parked = new Vector2(-9999f, -9999f);

        struct Row
        {
            public string Session;
            public Pawn Pawn;
            public Rect Line;   // the whole row, for the highlight and the hover
            public Rect Text;   // beside the portrait: the three labels, and what a click takes
            public Rect Face;   // the square face box, read by the drawer patch

            public bool Ghost;
            public bool Worker;
        }

        struct Head
        {
            public string Label;
            public Rect Rect;   // the whole band, so the arrow and the name click as one
            public int Active;
            public int Total;
            public bool Folded;
        }

        static readonly SidebarLayout Layout = new SidebarLayout();
        static readonly SidebarInteraction Interaction = new SidebarInteraction();
        static readonly SidebarProjectState Projects = new SidebarProjectState();

        const string Loose = "no project";

        static HashSet<string> Folded => Projects.Folded;

        static void Fold(string key, bool on)
        {
            Projects.SetFolded(key, on);
        }

    }
}
