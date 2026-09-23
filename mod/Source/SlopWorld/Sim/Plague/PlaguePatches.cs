using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Prevent wild plant spawning in the full plague band unless an aura protects the cell.
    // Permit spawning in the weak band.
    [HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.CheckSpawnWildPlantAt))]
    public static class Patch_NoRegrowth
    {
        static bool Prefix(IntVec3 c, Map ___map, ref bool __result)
        {
            // Permit normal plant spawning when Grandma's visiting.
            if (Settings.GrandmaMode) return true;

            var plague = ___map?.GetComponent<Plague>();
            if (plague == null || plague.BandAt(c) != Plague.Band.Full) return true;

            // Permit spawning in cells with aura protection.
            if (Aura.Of(___map)?.Covers(c) == true) return true;

            __result = false;
            return false;
        }
    }

    // Control fire spread attempts based on the fire position.
    // TrySpread selects its target internally, so the patch can only permit or block the whole attempt.
    // Permit all attempts during the NextPlanet scene.
    [HarmonyPatch(typeof(Fire), "TrySpread")]
    public static class Patch_ContainFire
    {
        static bool Prefix(Fire __instance)
        {
            if (NextPlanet.Leaving) return true;

            var plague = __instance.Map?.GetComponent<Plague>();
            if (plague == null || !plague.Active) return true;
            // Block spread from fires inside the aura to protect nearby plants.
            if (Aura.Of(__instance.Map)?.Covers(__instance.Position) == true) return false;
            return plague.Reaches(__instance.Position);
        }
    }
}
