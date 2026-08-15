using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public partial class Worksite
    {
        // Vanilla's figures are an economy's; the errand table states seconds of an agent's
        // working time and this is where that lands. Anything off the table keeps vanilla's
        // number, WorkFor answering zero for it.
        [HarmonyPatch(typeof(Frame), nameof(Frame.WorkToBuild), MethodType.Getter)]
        public static class Patch_ErrandWork
        {
            static void Postfix(Frame __instance, ref float __result)
            {
                float work = WorkFor(__instance?.def?.entityDefToBuild);
                if (work > 0f) __result = work;
            }
        }

        // Where a finished thing becomes plague. A prefix, because after CompleteConstruction
        // the frame is despawned and has neither a map nor the footprint - and the footprint
        // is the point, a five-by-three machine being a source that wide rather than a point.
        [HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
        public static class Patch_ErrandDone
        {
            static void Prefix(Frame __instance)
            {
                if (__instance == null || !__instance.Spawned) return;

                var what = __instance.def?.entityDefToBuild;
                if (WorkFor(what) <= 0f) return;

                var map = __instance.Map;
                var rect = __instance.OccupiedRect();
                map?.GetComponent<Worksite>()?.Count(rect.Area);

                float bloom = BloomFor(what);
                if (bloom <= 0f) return;

                var plague = map?.GetComponent<Plague>();
                if (plague == null) return;
                foreach (var c in rect) plague.Bloom(c, bloom);
            }
        }

        // A terrain frame draws four white corner brackets, and paving is queued a square at
        // a time, so ahead of the agents that is a grid over most of the board saying
        // nothing anybody can act on. Anything with a shape keeps its frame.
        // By name: the override is not public, so nameof would not compile.
        [HarmonyPatch(typeof(Frame), "DrawAt", new[] { typeof(Vector3), typeof(bool) })]
        public static class Patch_HideFloorFrames
        {
            static bool Prefix(Frame __instance)
            {
                var what = __instance?.def?.entityDefToBuild;
                return !(what is TerrainDef) || WorkFor(what) <= 0f;
            }
        }
    }
}
