using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Convert durations from these GenDate helpers to real time through RealClock.
    // The base game helpers use calendar units of 2500 ticks per hour and 60000 ticks per day.

    // Format the duration with normal or short unit names.
    [HarmonyPatch(typeof(GenDate), nameof(GenDate.ToStringTicksToPeriod))]
    public static class Patch_TicksToPeriod
    {
        static bool Prefix(int numTicks, bool allowSeconds, bool shortForm, bool canUseDecimals,
            bool canUseDecimalsShortForm, ref string __result)
        {
            __result = RealClock.GameTickDuration(numTicks, allowSeconds, shortForm, canUseDecimals, canUseDecimalsShortForm);
            return false;
        }
    }

    // Replace the verbose calendar duration with a real-time duration.
    [HarmonyPatch(typeof(GenDate), nameof(GenDate.ToStringTicksToPeriodVerbose))]
    public static class Patch_TicksToPeriodVerbose
    {
        static bool Prefix(int numTicks, bool allowHours, ref string __result)
        {
            __result = RealClock.GameTickDuration(numTicks, allowHours: allowHours);
            return false;
        }
    }

    // Replace approximate calendar durations with real-time durations.
    [HarmonyPatch(typeof(GenDate), nameof(GenDate.ToStringTicksToPeriodVague))]
    public static class Patch_TicksToPeriodVague
    {
        static bool Prefix(int numTicks, ref string __result)
        {
            __result = RealClock.GameTickDuration(numTicks);
            return false;
        }
    }

    // Replace the days-only format with the appropriate real-time unit.
    [HarmonyPatch(typeof(GenDate), nameof(GenDate.ToStringTicksToDays))]
    public static class Patch_TicksToDays
    {
        static bool Prefix(int numTicks, string format, ref string __result)
        {
            __result = RealClock.GameTickDuration(numTicks, format: format);
            return false;
        }
    }

    [HarmonyPatch(typeof(LogEntry), MethodType.Constructor, typeof(LogEntryDef))]
    public static class Patch_LogEntryCreated
    {
        static void Postfix(LogEntry __instance) => LogEntryClock.Created(__instance);
    }

    [HarmonyPatch(typeof(LogEntry), nameof(LogEntry.ExposeData))]
    public static class Patch_LogEntryClockSave
    {
        static void Postfix(LogEntry __instance) => LogEntryClock.Expose(__instance);
    }

    // Keep log ages independent of the solar calendar.
    [HarmonyPatch(typeof(LogEntry), nameof(LogEntry.GetTipString))]
    public static class Patch_LogEntryTip
    {
        static bool Prefix(LogEntry __instance, ref string __result)
        {
            if (__instance.Timestamp < 0) return true; // never stamped

            string ago = RealClock.Period(LogEntryClock.SecondsSince(__instance));
            __result = "OccurredTimeAgo".Translate(ago).CapitalizeFirst() + ".";
            return false;
        }
    }
}
