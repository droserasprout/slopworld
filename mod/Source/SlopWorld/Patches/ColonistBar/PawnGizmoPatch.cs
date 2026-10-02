using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>The mod does not assign <c>PriorityWork</c>. A saved cell with zero coordinates
    /// can still produce a "Clear prioritized work" button. Remove that unused action.</summary>
    [HarmonyPatch(typeof(PriorityWork), nameof(PriorityWork.GetGizmos))]
    public static class Patch_NoPrioritizedWorkGizmo
    {
        static readonly FieldInfo PawnField = AccessTools.Field(typeof(PriorityWork), "pawn");

        // Log one error if the reflected field is unavailable.
        static readonly bool Ready = Check();

        static bool Check()
        {
            if (PawnField != null) return true;
            Log.Error("[SlopWorld] PriorityWork.pawn moved. The work gizmo stays.");
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
