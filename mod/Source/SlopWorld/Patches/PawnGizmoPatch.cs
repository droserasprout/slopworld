using System.Collections.Generic;
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
                        () => SessionHub.Instance.Stop(session, Fail),
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
                    action = () => SessionHub.Instance.Start(session, Fail),
                };
            }
        }

        static void Fail(string msg) =>
            Messages.Message($"SlopWorld: {msg}", MessageTypeDefOf.RejectInput, false);
    }
}
