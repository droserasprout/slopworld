using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // `tools/icons.py` generates Codicon PNG slots. Keep the manifest and lookup table
    // in sync. ContentFinder roots these textures. Only runtime-generated textures need rooting.
    [StaticConstructorOnStartup]
    public static class Icons
    {
        const string Dir = "SlopWorld/Icons/";

        // Lazily loaded and cached, misses included: ContentFinder walks the mod's content
        // tables, and a sidebar is dozens of icons a frame.
        static readonly Dictionary<string, Texture2D> Cache =
            new Dictionary<string, Texture2D>();

        // ------------------------------------------------------------------ the slots

        public static Texture2D Terminal => Get("terminal");

        public static Texture2D Display => Get("display");

        public static Texture2D Gear => Get("gear");

        // The top bar's config door is the same cog as the options row's. One glyph, two
        // names, because the two call sites are not about the same thing.
        public static Texture2D Config => Gear;

        public static Texture2D Shield => Get("shield");

        public static Texture2D Play => Get("play");

        public static Texture2D Stop => Get("stop");

        public static Texture2D Check => Get("check");

        public static Texture2D Cross => Get("cross");

        public static Texture2D Dot => Get("dot");

        // The icon set uses one scale factor based on its largest glyph.
        // Each shape is centered in its square. This smallest glyph uses 30 of 64 pixels.
        // A rect at the required diameter would otherwise produce a glyph at less than half that size.
        // Callers that need a circle of a given size ask for the texture's rect here.
        const float DotInk = 30f / 64f;

        public static Rect DotBox(Vector2 center, float diameter)
        {
            float box = diameter / DotInk;
            return new Rect(center.x - box / 2f, center.y - box / 2f, box, box);
        }

        public static Texture2D Agents => Get("agents");

        public static Texture2D Files => Get("files");

        public static Texture2D Search => Get("search");

        public static Texture2D Git => Get("git");

        public static Texture2D Library => Get("library");

        public static Texture2D Tasks => Check;

        public static Texture2D Keyboard => Get("keyboard");

        // The dotfile switch and a row's view action. Reading is what both are about, and
        // a second eye under another name would be the same pixels.
        public static Texture2D Hidden => Get("eye");

        public static Texture2D View => Hidden;

        // The sidebar's project filter, which every view is read through.
        public static Texture2D Filter => Get("filter");

        public static Texture2D Refresh => Get("refresh");

        public static Texture2D Bell => Get("bell");

        public static Texture2D Menu => Get("menu");

        public static Texture2D Edit => Get("edit");

        public static Texture2D Diff => Get("diff");

        public static Texture2D Type => Get("type");

        public static Texture2D Trophy => Get("trophy");

        public static Texture2D RimWorld => Get("rimworld");

        // The options column's Usage row. A card rather than the silver the readouts draw:
        // those are the game's own resource icons on purpose, and this row is a tab.
        public static Texture2D Usage => Get("usage");

        // The options column's Integrations row. A link names host services and credentials.
        // the credit card is reserved for the Usage page itself.
        public static Texture2D Link => Get("link");

        public static Texture2D Time => Get("time");

        public static Texture2D Add => Get("add");

        // ------------------------------------------------------------------ the loader

        static Texture2D Get(string slot)
        {
            if (Cache.TryGetValue(slot, out var tex)) return tex;

            // BadTex rather than null on a miss. A file-type icon that fails to load is a
            // blank cell in a list. A gizmo icon that fails to load is a button nobody can
            // see, which is the exact trap the drawn icons were written to avoid.
            tex = ContentFinder<Texture2D>.Get(Dir + slot, false) ?? BaseContent.BadTex;
            Cache[slot] = tex;
            return tex;
        }
    }
}
