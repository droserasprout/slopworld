using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>Selecting an agent's colonist gives you a button into its terminal.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos
    {
        static Texture2D _icon;

        // A solid swatch avoids shipping a texture and reads fine at gizmo size.
        static Texture2D Icon => _icon != null
            ? _icon
            : _icon = SolidColorMaterials.NewSolidColorTexture(new Color(0.12f, 0.14f, 0.16f));

        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn __instance)
        {
            foreach (var g in gizmos) yield return g;

            var colony = AgentColony.Current;
            var session = colony?.SessionOf(__instance);
            if (session == null) yield break;

            var info = SessionHub.Instance.Get(session);
            var state = info?.State ?? AgentState.Dead;

            yield return new Command_Action
            {
                defaultLabel = "Terminal",
                defaultDesc = $"Open the terminal for '{session}'.\nState: {state.ToString().ToLower()}",
                icon = Icon,
                defaultIconColor = TerminalWindow.StateColor(state),
                action = () => TerminalWindow.Open(session),
            };
        }
    }
}
