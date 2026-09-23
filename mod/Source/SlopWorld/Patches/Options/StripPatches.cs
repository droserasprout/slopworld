using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // Disable selected simulation systems by skipping their tick methods.
    // These targets use RimWorld 1.6 interval methods and do not apply to 1.5.

    // Skip needs updates.
    [HarmonyPatch(typeof(Pawn_NeedsTracker), nameof(Pawn_NeedsTracker.NeedsTrackerTickInterval))]
    public static class Patch_Needs
    {
        static bool Prefix() => false;
    }

    // Skip health condition updates.
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.HealthTickInterval))]
    public static class Patch_Health
    {
        static bool Prefix() => false;
    }

    // Skip pawn aging.
    [HarmonyPatch(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.AgeTickInterval))]
    public static class Patch_Age
    {
        static bool Prefix() => false;
    }

    // Skip mental break checks.
    [HarmonyPatch(typeof(MentalBreaker), nameof(MentalBreaker.MentalBreakerTickInterval))]
    public static class Patch_MentalBreaker
    {
        static bool Prefix() => false;
    }

    // Prevent storyteller ticks from starting raids, events, quests, or weather disasters.
    [HarmonyPatch(typeof(Storyteller), nameof(Storyteller.StorytellerTick))]
    public static class Patch_Storyteller
    {
        static bool Prefix() => false;
    }

    // Skip automatic social interaction updates.
    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.InteractionsTrackerTickInterval))]
    public static class Patch_Interactions
    {
        static bool Prefix() => false;
    }
}
