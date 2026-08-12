using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>Pawn gizmos stay vanilla; session actions come from the current
    /// <see cref="SessionSelectable"/> inserted by <see cref="SessionGizmoSelection"/>.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos
    {
        // Empty - the pawn shows whatever vanilla gave it; session gizmos come from
        // the SessionSelectable on the gizmo grid.
    }

    /// <summary><c>PriorityWork</c> is never assigned by this mod, but a zeroed saved cell
    /// can make vanilla emit "Clear prioritized work"; remove the no-op button.</summary>
    [HarmonyPatch(typeof(PriorityWork), nameof(PriorityWork.GetGizmos))]
    public static class Patch_NoPrioritizedWorkGizmo
    {
        static readonly FieldInfo PawnField = AccessTools.Field(typeof(PriorityWork), "pawn");

        // A reflection target that stops resolving must be loud once, not silently inert.
        static readonly bool Ready = Check();

        static bool Check()
        {
            if (PawnField != null) return true;
            Log.Error("[SlopWorld] PriorityWork.pawn moved; the work gizmo stays");
            return false;
        }

        static bool Prefix(PriorityWork __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!Ready || !AgentColony.IsAgent(PawnField.GetValue(__instance) as Pawn))
                return true;

            __result = Enumerable.Empty<Gizmo>();
            return false;
        }
    }
}
