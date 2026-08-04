using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>Selecting an agent's colonist gives you a button into its terminal.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn __instance)
        {
            foreach (var g in gizmos) yield return g;

            var colony = AgentColony.Current;
            var session = colony?.SessionOf(__instance);
            if (session == null) yield break;

            var info = SessionHub.Instance.Get(session);
            if (info == null) yield break; // the daemon has never heard of it
            var state = info.State;

            if (info.Alive) // nothing to type at while the process is down
            {
                yield return new Command_Action
                {
                    defaultLabel = "Terminal",
                    defaultDesc = $"Open the terminal for '{session}'.\nState: {state.ToString().ToLower()}",
                    icon = TerminalIcon.Tex,
                    defaultIconColor = TerminalWindow.StateColor(state),
                    hotKey = SlopDefOf.SlopOpenTerminal,
                    action = () => TerminalWindow.Open(session),
                };

                yield return new Command_Action
                {
                    defaultLabel = "Stop",
                    defaultDesc = $"Stop '{session}'. The colonist stays on the floor "
                                + "until the process runs again.",
                    icon = PowerIcon.StopTex,
                    defaultIconColor = new Color(0.90f, 0.45f, 0.42f),
                    hotKey = SlopDefOf.SlopToggleSession,
                    action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Stop '{session}'? This kills the tmux session; whatever the agent "
                      + "is in the middle of goes with it.",
                        () => SessionHub.Instance.Stop(session, SlopWidgets.Fail),
                        destructive: true)),
                };
            }
            else
            {
                yield return new Command_Action
                {
                    defaultLabel = "Start",
                    defaultDesc = $"Start '{session}' and put its colonist back on its feet.",
                    icon = PowerIcon.StartTex,
                    defaultIconColor = new Color(0.55f, 0.82f, 0.55f),
                    hotKey = SlopDefOf.SlopToggleSession,
                    action = () => SessionHub.Instance.Start(session, SlopWidgets.Fail),
                };
            }
        }

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
