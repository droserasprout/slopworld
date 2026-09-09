using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Each map manager owns its deadline. Weak keys release discarded maps automatically.
    static class HiddenMapUpdates
    {
        static readonly ConditionalWeakTable<object, HiddenWorkCadence> Cadences =
            new ConditionalWeakTable<object, HiddenWorkCadence>();
        static readonly ConditionalWeakTable<object, HiddenWorkCadence>.CreateValueCallback Create =
            _ => new HiddenWorkCadence();

        static bool Begin(object manager, string skipped, out long started)
        {
            started = 0;
            if (!Cadences.GetValue(manager, Create).Run(PaneOverDraw.Hidden, Time.realtimeSinceStartupAsDouble))
            {
                PerfTrace.Count(skipped);
                return false;
            }
            started = PerfTrace.Start();
            return true;
        }

        [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.MapMeshDrawerUpdate_First))]
        static class Mesh
        {
            static bool Prefix(MapDrawer __instance, out long __state) =>
                Begin(__instance, "hidden-mesh-skips", out __state);
            static void Postfix(long __state) => PerfTrace.End("map-mesh-maintenance", __state, 1);
        }

        [HarmonyPatch(typeof(SkyManager), nameof(SkyManager.SkyManagerUpdate))]
        static class Sky
        {
            static bool Prefix(SkyManager __instance, out long __state) =>
                Begin(__instance, "hidden-sky-skips", out __state);
            static void Postfix(long __state) => PerfTrace.End("map-sky", __state, 1);
        }

        // Real-time flecks must still expire. Measure this separately from their suppressed draw.
        [HarmonyPatch(typeof(FleckManager), nameof(FleckManager.FleckManagerUpdate))]
        static class Flecks
        {
            static void Prefix(out long __state) => __state = PerfTrace.Start();
            static void Postfix(long __state) => PerfTrace.End("map-fleck-aging", __state, 1);
        }
    }
}
