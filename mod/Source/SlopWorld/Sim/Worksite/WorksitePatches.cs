using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public partial class Worksite
    {
        // Use work requirements from the errand table for agent construction.
        // Keep the base game value when WorkFor returns zero.
        [HarmonyPatch(typeof(Frame), nameof(Frame.WorkToBuild), MethodType.Getter)]
        public static class Patch_ErrandWork
        {
            static void Postfix(Frame __instance, ref float __result)
            {
                float work = WorkFor(__instance?.def?.entityDefToBuild);
                if (work > 0f) __result = work;
            }
        }

        // Add plague growth across the completed building footprint.
        // Run before CompleteConstruction removes the frame and its map reference.
        [HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
        public static class Patch_ErrandDone
        {
            static void Prefix(Frame __instance)
            {
                if (__instance == null || !__instance.Spawned) return;

                var what = __instance.def?.entityDefToBuild;
                if (WorkFor(what) <= 0f) return;

                var map = __instance.Map;
                map?.GetComponent<Worksite>()?.MarkFramesDirty();
                var rect = __instance.OccupiedRect();
                map?.GetComponent<Worksite>()?.Count(rect.Area);

                float bloom = BloomFor(what);
                if (bloom <= 0f) return;

                var plague = map?.GetComponent<Plague>();
                if (plague == null) return;
                foreach (var c in rect) plague.Bloom(c, bloom);
            }
        }

        // Hide terrain frame brackets for worksite paving to reduce visual clutter.
        // Keep building frames visible.
        // Use the method name as a string because the override is not public.
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
