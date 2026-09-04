using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    [HarmonyLib.HarmonyPatch(typeof(GenMapUI), nameof(GenMapUI.DrawPawnLabel),
        new[] { typeof(Pawn), typeof(Vector2), typeof(float), typeof(float),
                typeof(Dictionary<string, string>), typeof(GameFont), typeof(bool),
                typeof(bool) })]
    public static class Patch_SidebarPawnLabel
    {
        // The map label rect is deliberately much taller than one line. With a custom large
        // font, Widgets.Label's default wrapping turns a pawn name into a two-line label.
        // Keep map names single-line, and restore the shared text state even if drawing fails.
        static bool Prefix(Pawn pawn, out bool __state)
        {
            __state = Text.WordWrap;
            Text.WordWrap = false;
            return !AgentSidebar.Drawing && !PlayerPawn.IsPlayer(pawn);
        }

        static Exception Finalizer(Exception __exception, bool __state)
        {
            Text.WordWrap = __state;
            return __exception;
        }
    }

    // GenMapUI has a second overload that accepts the final background rect. Keep the player
    // label hidden when another map path enters at that overload directly.
    [HarmonyLib.HarmonyPatch(typeof(GenMapUI), nameof(GenMapUI.DrawPawnLabel),
        new[] { typeof(Pawn), typeof(Rect), typeof(float), typeof(float),
                typeof(Dictionary<string, string>), typeof(GameFont), typeof(bool),
                typeof(bool) })]
    public static class Patch_SidebarPawnLabelRect
    {
        static bool Prefix(Pawn pawn, out bool __state)
        {
            __state = Text.WordWrap;
            Text.WordWrap = false;
            return !AgentSidebar.Drawing && !PlayerPawn.IsPlayer(pawn);
        }

        static Exception Finalizer(Exception __exception, bool __state)
        {
            Text.WordWrap = __state;
            return __exception;
        }
    }

    // Vanilla asks this utility for the map name color. Reuse the state palette already used
    // by the sidebar and terminal, so a name remains useful after the state word is gone.
    [HarmonyLib.HarmonyPatch(typeof(PawnNameColorUtility), nameof(PawnNameColorUtility.PawnNameColorOf))]
    public static class Patch_AgentPawnNameColor
    {
        static void Postfix(Pawn pawn, ref Color __result)
        {
            if (!AgentColony.IsAgent(pawn)) return;

            string session = AgentColony.Current?.SessionOf(pawn);
            var state = session == null
                ? AgentState.Down
                : SessionHub.Instance.Get(session)?.State ?? AgentState.Down;
            __result = TerminalWindow.StateColor(state);
        }
    }
}
