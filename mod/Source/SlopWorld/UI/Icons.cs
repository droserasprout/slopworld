using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Every action icon the mod draws, in one place. The textures are Codicons - VS
    // Code's icon set - baked out of a Nerd Font by tools/icons.py, landing in
    // mod/Textures/SlopWorld/Icons/<slot>.png; this is the other half of
    // tools/icons/manifest.toml and the two are kept in step by hand, a table being
    // cheaper here than shipping the manifest into the game and parsing TOML with no
    // parser. Same arrangement as FileIcons and its manifest.
    //
    // These used to be nine classes of pixel math, each drawing its one shape from
    // predicates and signed distances. The reason given was that vanilla keeps its art
    // in asset bundles, so a content path like TexButton's resolves to null and draws a
    // button nobody can see - but that is about *vanilla* paths. The mod's own Textures
    // tree is loose and ContentFinder reads it, which FileIcons had been proving all
    // along. What the hand-written shapes actually cost was a coverage budget every new
    // icon had to be tuned against by eye, written down in the old TabIcons because five
    // shapes had to agree on it. A designed set on one grid agrees by construction.
    //
    // Named for the slot rather than the glyph: the agents tab is the agents tab whatever
    // picture it wears next year, and a call site reading Icons.Agents does not have to
    // be revisited when that changes.
    //
    // The attribute only quiets the startup scan, which warns about any type holding a
    // static Texture2D. Nothing here builds a texture, so nothing here needs
    // HideFlags.DontUnloadUnusedAsset: ContentFinder's textures are rooted by the content
    // tables, and it is the ones built at runtime - Slab's corners, DeadCursor - that the
    // unload on a map switch would take.
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

        public static Texture2D Agents => Get("agents");

        public static Texture2D Files => Get("files");

        public static Texture2D Git => Get("git");

        public static Texture2D Shortcuts => Get("shortcuts");

        // The dotfile switch and a row's view action. Reading is what both are about, and
        // a second eye under another name would be the same pixels.
        public static Texture2D Hidden => Get("eye");

        public static Texture2D View => Hidden;

        public static Texture2D Refresh => Get("refresh");

        public static Texture2D Bell => Get("bell");

        public static Texture2D Menu => Get("menu");

        public static Texture2D Edit => Get("edit");

        public static Texture2D Diff => Get("diff");

        public static Texture2D Type => Get("type");

        public static Texture2D Trophy => Get("trophy");

        // The sidebar's one add button, which used to be a "+" in GameFont.Medium: a
        // glyph is drawn heavier than the font's plus and can be given whatever size the
        // strip has room for, the font's largest being all vanilla had to offer.
        public static Texture2D Add => Get("add");

        // ------------------------------------------------------------------ the loader

        static Texture2D Get(string slot)
        {
            if (Cache.TryGetValue(slot, out var tex)) return tex;

            // BadTex rather than null on a miss. A file-type icon that fails to load is a
            // blank cell in a list; a gizmo icon that fails to load is a button nobody can
            // see, which is the exact trap the drawn icons were written to avoid.
            tex = ContentFinder<Texture2D>.Get(Dir + slot, false) ?? BaseContent.BadTex;
            Cache[slot] = tex;
            return tex;
        }
    }
}
