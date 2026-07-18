using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // The sim is stripped by declining to tick it, not by patching out individual
    // systems, so everything stays consistent and the toggle works at runtime.
    //
    // RimWorld 1.6 moved most of these to interval ticks (delta = ticks elapsed),
    // so the target names below are the 1.6 ones and will not bind on 1.5.

    /// <summary>No hunger, no rest, no joy decay. Needs stay wherever they started.</summary>
    [HarmonyPatch(typeof(Pawn_NeedsTracker), nameof(Pawn_NeedsTracker.NeedsTrackerTickInterval))]
    public static class Patch_Needs
    {
        static bool Prefix() => !Settings.StripSim;
    }

    /// <summary>No disease, no hypothermia, no bleeding out, no hediff progression.</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.HealthTickInterval))]
    public static class Patch_Health
    {
        static bool Prefix() => !Settings.StripSim;
    }

    /// <summary>Agents do not get old.</summary>
    [HarmonyPatch(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.AgeTickInterval))]
    public static class Patch_Age
    {
        static bool Prefix() => !Settings.StripSim;
    }

    /// <summary>No breakdowns, tantrums or wandering-in-sadness.</summary>
    [HarmonyPatch(typeof(MentalBreaker), nameof(MentalBreaker.MentalBreakerTickInterval))]
    public static class Patch_MentalBreaker
    {
        static bool Prefix() => !Settings.StripSim;
    }

    /// <summary>The one that matters: no raids, no events, no quests, no weather disasters.</summary>
    [HarmonyPatch(typeof(Storyteller), nameof(Storyteller.StorytellerTick))]
    public static class Patch_Storyteller
    {
        static bool Prefix() => !Settings.StripSim;
    }
}
