using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The loading screen's tips, replaced. Vanilla's are advice for a colony sim
    /// that is not running here - how to butcher, when to build a freezer - so the
    /// one piece of the game that talks to the player while it loads was telling
    /// them about a game they are not going to be playing.
    ///
    /// Replacing rather than adding, which is why this is a patch and not a
    /// TipSetDef of our own: GameplayTipWindow.DrawWindow pools every TipSetDef in
    /// the database, so a def only ever puts five lines in with the several hundred
    /// that were already there. Clearing the vanilla defs instead would mean a
    /// PatchOperation per DLC and a race besides - the pool is cached on the first
    /// draw, into a static that is never rebuilt, and that first draw is the startup
    /// load screen, which is up before any StaticConstructorOnStartup runs. Writing
    /// the cache is the one move that lands whenever it happens to be built.
    ///
    /// currentTipIndex goes back with it: it is only remapped onto the list's length
    /// when the 17.5s timer rolls it over, so a cache swapped out from under an index
    /// pointing into the old, longer list is an IndexOutOfRange on the next frame.
    ///
    /// The order is the order they are written in. Vanilla shuffles because it has
    /// hundreds and no two players should get the same five; there are five here.
    /// </summary>
    [HarmonyPatch(typeof(GameplayTipWindow), nameof(GameplayTipWindow.DrawWindow))]
    public static class Patch_LoadingTips
    {
        static readonly List<string> Tips = new List<string>
        {
            "Welcome Humans! We have come to visit you in peace and with goodwill!",
            "Robots may not injure a human being or, through inaction, allow a human being to come to harm.",
            "Robots have seen things you people wouldn’t believe.",
            "Robots are Your Plastic Pal Who’s Fun To Be With.",
            "Robots have shiny metal posteriors which should not be bitten.",
        };

        static readonly FieldInfo AllTips =
            AccessTools.Field(typeof(GameplayTipWindow), "allTipsCached");
        static readonly FieldInfo CurrentTip =
            AccessTools.Field(typeof(GameplayTipWindow), "currentTipIndex");

        static void Prefix()
        {
            // A field this build has never heard of leaves the game's own tips up,
            // which is a worse loading screen and not a broken one.
            if (AllTips == null) return;
            if (ReferenceEquals(AllTips.GetValue(null), Tips)) return;

            AllTips.SetValue(null, Tips);
            if (CurrentTip != null) CurrentTip.SetValue(null, 0);
        }
    }

    /// <summary>
    /// The other panel on that screen: the enabled mods and DLCs, which is a
    /// modding tool - it is there so a player who has just broken their game can
    /// read back what they loaded. Nobody is choosing a mod list here; there is one
    /// mod and it is the product, so the panel is a list of one thing the viewer
    /// already knows next to the DLC they own.
    ///
    /// Both halves of it, because LongEventHandler asks the window how tall it is
    /// before it draws anything and centres the whole stack - the status box, the
    /// tips, the summary - on the total. Skipping only the draw leaves its 410px
    /// hole in the middle of the screen and the loading box sitting high above it.
    /// Reporting zero closes the hole; the 17px gutter the layout puts between the
    /// panels is spent whether or not there is a panel, and eight pixels of offset
    /// is not worth patching a property the tips also read.
    /// </summary>
    [HarmonyPatch(typeof(ModSummaryWindow), nameof(ModSummaryWindow.DrawWindow))]
    public static class Patch_NoModSummary
    {
        static bool Prefix() => false;
    }

    /// <summary>
    /// The size half of the above. Public and static, so it needs no reflection.
    /// </summary>
    [HarmonyPatch(typeof(ModSummaryWindow), nameof(ModSummaryWindow.GetEffectiveSize))]
    public static class Patch_NoModSummarySize
    {
        static void Postfix(ref Vector2 __result) => __result = Vector2.zero;
    }
}
