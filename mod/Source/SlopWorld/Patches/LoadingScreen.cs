using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
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
    /// hundreds and no two players should get the same five; there are six here.
    ///
    /// The rotation is ours too. Vanilla's tipUpdateInterval is a const inlined into
    /// DrawContents, so there is no field to write - but there is no need for one:
    /// the timer it compares against is a field, and stamping that with the current
    /// time on every draw means vanilla's own 17.5s never elapses and the index only
    /// ever moves when we move it. Six tips at TipSeconds each is a handful read
    /// rather than one tip stared at, which is what 17.5 gets you on a load that
    /// takes half a minute.
    /// </summary>
    [HarmonyPatch(typeof(GameplayTipWindow), nameof(GameplayTipWindow.DrawWindow))]
    public static class Patch_LoadingTips
    {
        // Long enough to read, short enough that a load shows several.
        const float TipSeconds = 5f;

        static readonly List<string> Tips = new List<string>
        {
            "Welcome Humans! We have come to visit you in peace and with goodwill!",
            "Robots may not injure a human being or, through inaction, allow a human being to come to harm.",
            "Robots have seen things you people wouldn’t believe.",
            "Robots are Your Plastic Pal Who’s Fun To Be With.",
            "Robots have shiny metal posteriors which should not be bitten.",
            // F12 is spelled out rather than read off SlopQuickTerminal: the first
            // loading screen is up before any def is loaded, so there is nothing to
            // read the label from at the point this list is installed.
            "Press F12 to open an agent's terminal, and F12 again to leave it.",
        };

        static readonly FieldInfo AllTips =
            AccessTools.Field(typeof(GameplayTipWindow), "allTipsCached");
        static readonly FieldInfo CurrentTip =
            AccessTools.Field(typeof(GameplayTipWindow), "currentTipIndex");
        static readonly FieldInfo LastRotated =
            AccessTools.Field(typeof(GameplayTipWindow), "lastTimeUpdatedTooltip");

        static float _shown;

        static void Prefix()
        {
            // A field this build has never heard of leaves the game's own tips up,
            // which is a worse loading screen and not a broken one.
            if (AllTips == null) return;

            float now = Time.realtimeSinceStartup;
            if (!ReferenceEquals(AllTips.GetValue(null), Tips))
            {
                AllTips.SetValue(null, Tips);
                if (CurrentTip != null) CurrentTip.SetValue(null, 0);
                _shown = now;
            }
            else if (CurrentTip != null && now - _shown >= TipSeconds)
            {
                CurrentTip.SetValue(null, ((int)CurrentTip.GetValue(null) + 1) % Tips.Count);
                _shown = now;
            }

            // Holding vanilla's timer at now is what keeps it from rolling the index
            // over underneath us on its own schedule.
            if (LastRotated != null) LastRotated.SetValue(null, now);
        }
    }

    /// <summary>
    /// The loading screen itself: the tips, centred, and nothing else. What goes is
    /// the status box above them - the one that names the event being waited on and
    /// draws a bar for it. It is a progress readout for a colony sim's own loading,
    /// and the half of it that is honest ("Loading...") the tips panel already
    /// implies by being on screen at all.
    ///
    /// A prefix rather than a transpiler, and a re-layout rather than a hidden box,
    /// because LongEventsOnGUI centres the whole stack on the sum of the heights it
    /// is going to draw - the same trap ModSummaryWindow's hole was. Declining to
    /// draw the box would leave its 120-odd pixels above the tips and the tips low
    /// on the screen.
    ///
    /// It only takes over the screen it was asked about. Vanilla runs on for the
    /// standard-window path (the small in-game box during a save, which never had
    /// tips under it), for a long event that asked for no extra UI (where the box is
    /// the only thing on screen and taking it away leaves what reads as a hang), and
    /// for any build where one of the fields below has moved.
    /// </summary>
    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsOnGUI))]
    public static class Patch_LoadingLayout
    {
        static readonly FieldInfo CurrentEvent =
            AccessTools.Field(typeof(LongEventHandler), "currentEvent");
        static readonly Type EventType = CurrentEvent?.FieldType;
        static readonly FieldInfo ForceHideUI =
            EventType == null ? null : AccessTools.Field(EventType, "forceHideUI");
        static readonly FieldInfo ShowExtraUIInfo =
            EventType == null ? null : AccessTools.Field(EventType, "showExtraUIInfo");
        static readonly MethodInfo UseStandardWindow =
            EventType == null ? null : AccessTools.PropertyGetter(EventType, "UseStandardWindow");

        static bool Prefix()
        {
            if (ForceHideUI == null || ShowExtraUIInfo == null || UseStandardWindow == null) return true;

            object ev = CurrentEvent.GetValue(null);
            if (ev == null) return true;                                // vanilla resets the tip timer
            if ((bool)ForceHideUI.GetValue(ev)) return true;            // vanilla draws nothing
            if ((bool)UseStandardWindow.Invoke(ev, null)) return true;  // the in-game box, not this screen
            if (Find.UIRoot == null) return true;
            if (!(bool)ShowExtraUIInfo.GetValue(ev)) return true;

            if (UIMenuBackgroundManager.background == null)
                UIMenuBackgroundManager.background = new UI_BackgroundMain();
            UIMenuBackgroundManager.background.BackgroundOnGUI();

            Vector2 size = GameplayTipWindow.WindowSize;
            GameplayTipWindow.DrawWindow(
                new Vector2((UI.screenWidth - size.x) / 2f, (UI.screenHeight - size.y) / 2f), false);
            return false;
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
