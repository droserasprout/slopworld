using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The pawn's own gizmos are vanilla only. Terminal / Start / Stop now live on
    /// <see cref="SessionSelectable"/>, which is inserted into the gizmo grid by
    /// <see cref="SessionGizmoSelection"/> for the current session.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos
    {
        // Empty - the pawn shows whatever vanilla gave it; session gizmos come from
        // the SessionSelectable on the gizmo grid.
    }

    /// <summary>
    /// "Clear prioritized work" offers to undo an order nobody gave. Work here is handed
    /// out by <see cref="Worksite"/> off the daemon's word, never through the priority
    /// system, so the button has nothing to clear - and it turns up anyway, because a
    /// PriorityWork that was never set reads back from a save with a zeroed cell and
    /// IntVec3 counts a zero as valid. A row on an agent's gizmo bar that does nothing is
    /// a row that says the player has a lever here.
    /// </summary>
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
