using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // The sim is stripped by declining to tick it rather than by patching out
    // individual systems, so everything stays consistent underneath. 1.6 moved most
    // of these to interval ticks, so the target names will not bind on 1.5.

    // No hunger, no rest, no joy decay.
    [HarmonyPatch(typeof(Pawn_NeedsTracker), nameof(Pawn_NeedsTracker.NeedsTrackerTickInterval))]
    public static class Patch_Needs
    {
        static bool Prefix() => false;
    }

    // No disease, no hypothermia, no bleeding out, no hediff progression.
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.HealthTickInterval))]
    public static class Patch_Health
    {
        static bool Prefix() => false;
    }

    // Agents do not get old.
    [HarmonyPatch(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.AgeTickInterval))]
    public static class Patch_Age
    {
        static bool Prefix() => false;
    }

    // No breakdowns, tantrums or wandering-in-sadness.
    [HarmonyPatch(typeof(MentalBreaker), nameof(MentalBreaker.MentalBreakerTickInterval))]
    public static class Patch_MentalBreaker
    {
        static bool Prefix() => false;
    }

    // The one that matters: no raids, no events, no quests, no weather disasters.
    [HarmonyPatch(typeof(Storyteller), nameof(Storyteller.StorytellerTick))]
    public static class Patch_Storyteller
    {
        static bool Prefix() => false;
    }

    // Pawns stop initiating social interactions, so nothing lands in the interaction
    // log.
    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.InteractionsTrackerTickInterval))]
    public static class Patch_Interactions
    {
        static bool Prefix() => false;
    }
}
