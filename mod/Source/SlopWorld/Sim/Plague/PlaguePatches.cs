using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Centralize wild-plant spawning: the sweep cannot keep up with the spawner, and only
    // Full-band cells reseed; weak-band plants must continue growing.
    [HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.CheckSpawnWildPlantAt))]
    public static class Patch_NoRegrowth
    {
        static bool Prefix(IntVec3 c, Map ___map, ref bool __result)
        {
            // Nothing to hold back where the circle grows things: sterilising the ground
            // under the flowerbeds would leave grandma mode a barer map than the rot does.
            if (Settings.GrandmaMode) return true;

            var plague = ___map?.GetComponent<Plague>();
            if (plague == null || plague.BandAt(c) != Plague.Band.Full) return true;

            // Except where the cat is standing.
            if (Aura.Of(___map)?.Covers(c) == true) return true;

            __result = false;
            return false;
        }
    }

    // Without this the bands are a lie the moment anything ignites: a rainforest carries
    // fire to the map edge in minutes. TrySpread picks its own cell internally, so this
    // can only allow or refuse the whole attempt. Off while NextPlanet is burning the map.
    [HarmonyPatch(typeof(Fire), "TrySpread")]
    public static class Patch_ContainFire
    {
        static bool Prefix(Fire __instance)
        {
            if (NextPlanet.Leaving) return true;

            var plague = __instance.Map?.GetComponent<Plague>();
            if (plague == null || !plague.Active) return true;
            // A fire under the cat goes out at the next sweep anyway; this stops it taking
            // the aura's plants with it.
            if (Aura.Of(__instance.Map)?.Covers(__instance.Position) == true) return false;
            return plague.Reaches(__instance.Position);
        }
    }
}
