using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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
    // Both spread paths call TryStartFireIn with the candidate cell and its map.
    // Rewrite only those calls so unrelated ignition retains its normal behavior.
    [HarmonyPatch]
    public static class Patch_FireSpreadDestination
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Fire), "TrySpread");
            yield return AccessTools.Method(typeof(Spark), "Impact");
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(FireUtility), nameof(FireUtility.TryStartFireIn));
            var replacement = AccessTools.Method(typeof(Patch_FireSpreadDestination), nameof(TrySpreadTo));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                }
                yield return instruction;
            }
        }

        // Recheck at spark impact: the aura or field can change while the spark travels.
        static bool TrySpreadTo(IntVec3 c, Map map, float fireSize, Thing instigator, SimpleCurve curve)
        {
            if (!NextPlanet.Leaving)
            {
                var plague = map?.GetComponent<Plague>();
                if (plague?.Active == true &&
                    (Aura.Of(map)?.Covers(c) == true || !plague.Reaches(c))) return false;
            }
            return FireUtility.TryStartFireIn(c, map, fireSize, instigator, curve);
        }
    }

}
