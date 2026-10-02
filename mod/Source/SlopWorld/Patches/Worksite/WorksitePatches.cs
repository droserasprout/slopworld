using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public partial class Worksite
    {
        // Ownership follows the frame through save/load without changing shared definitions.
        // Unmarked frames, including old saves, retain vanilla behavior.
        sealed class FrameOwnership
        {
            public FrameOwnership() { }
            public bool Owned;
        }

        static readonly ConditionalWeakTable<Frame, FrameOwnership> Ownership =
            new ConditionalWeakTable<Frame, FrameOwnership>();

        static bool Owns(Frame frame) =>
            frame != null && Ownership.TryGetValue(frame, out var marker) && marker.Owned;

        static void Claim(Frame frame) => Ownership.GetOrCreateValue(frame).Owned = true;

        [HarmonyPatch(typeof(Frame), nameof(Frame.ExposeData))]
        public static class Patch_FrameOwnership
        {
            static void Postfix(Frame __instance)
            {
                var marker = Ownership.GetOrCreateValue(__instance);
                Scribe_Values.Look(ref marker.Owned, "slopWorldWorksite", false);
            }
        }

        // Use work requirements from the errand table for agent construction.
        // Keep the base game value when WorkFor returns zero.
        [HarmonyPatch(typeof(Frame), nameof(Frame.WorkToBuild), MethodType.Getter)]
        public static class Patch_ErrandWork
        {
            static void Postfix(Frame __instance, ref float __result)
            {
                if (!Owns(__instance)) return;
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
                if (!Owns(__instance) || !__instance.Spawned) return;

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
                return !Owns(__instance) || !(what is TerrainDef) || WorkFor(what) <= 0f;
            }
        }
    }
}
