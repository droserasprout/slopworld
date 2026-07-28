using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Every duration RimWorld prints goes through one of the four GenDate helpers
    // below, and every one speaks the game's calendar: 2500 ticks to the hour, 60000
    // to the day. These rewrite the lot into the span a wall clock would have
    // measured, so the board reads in the same units as the agents it is watching.

    // The main one: "3 hours", "2 days", and the "5h" short form.
    [HarmonyPatch(typeof(GenDate), nameof(GenDate.ToStringTicksToPeriod))]
    public static class Patch_TicksToPeriod
    {
        static bool Prefix(int numTicks, bool shortForm, ref string __result)
        {
            __result = RealClock.Period(RealClock.Seconds(numTicks), shortForm);
            return false;
        }
    }

    // Vanilla spells this one out down to the hour ("2 days 5 hours"). One real unit
    // is plenty.
    [HarmonyPatch(typeof(GenDate), nameof(GenDate.ToStringTicksToPeriodVerbose))]
    public static class Patch_TicksToPeriodVerbose
    {
        static bool Prefix(int numTicks, ref string __result)
        {
            __result = RealClock.Period(RealClock.Seconds(numTicks));
            return false;
        }
    }

    // Vanilla rounds this one off at both ends ("less than a day"). Vagueness pitched
    // at a game day means nothing on a wall clock.
    [HarmonyPatch(typeof(GenDate), nameof(GenDate.ToStringTicksToPeriodVague))]
    public static class Patch_TicksToPeriodVague
    {
        static bool Prefix(int numTicks, ref string __result)
        {
            __result = RealClock.Period(RealClock.Seconds(numTicks));
            return false;
        }
    }

    // The days-only form. Every caller slots it into a sentence as a plain span, so
    // handing back the real unit reads fine.
    [HarmonyPatch(typeof(GenDate), nameof(GenDate.ToStringTicksToDays))]
    public static class Patch_TicksToDays
    {
        static bool Prefix(int numTicks, ref string __result)
        {
            __result = RealClock.Period(RealClock.Seconds(numTicks));
            return false;
        }
    }

    // The "occurred X ago" tooltip behind every line of a pawn's Log tab - the one
    // place a viewer reads a time at all. Unlike the raw durations above this one
    // knows when the thing happened, so it goes through the anchored lookup and gets
    // back the stretches the game clock stood still. The two vanilla overrides chain
    // up to this one.
    [HarmonyPatch(typeof(LogEntry), nameof(LogEntry.GetTipString))]
    public static class Patch_LogEntryTip
    {
        static bool Prefix(LogEntry __instance, ref string __result)
        {
            if (__instance.Timestamp < 0) return true; // never stamped

            string ago = RealClock.Period(RealClock.SecondsSince(__instance.Timestamp));
            __result = "OccurredTimeAgo".Translate(ago).CapitalizeFirst() + ".";
            return false;
        }
    }
}
