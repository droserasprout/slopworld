using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // An agent's colonist gets an Edit button in the tab row, and its directory and
    // state in the pane's own text.
    public static class InspectPaneAgent
    {
        // Mirrored from InspectPaneUtility: 72x30 slots laid right to left from the
        // pane's width, 30px above the pane top.
        const float TabW = 72f;
        const float TabH = 30f;

        // The session behind the single selected pawn, if it is an agent.
        public static string Selected()
        {
            // Find.Selector reaches through Find.MapUI, which casts the UI root to
            // UIRoot_Play; on the entry root that throws, and an exception here escapes
            // UIRootOnGUI before the window stack is drawn, leaving the screen blank.
            if (Current.ProgramState != ProgramState.Playing) return null;

            var pawn = Find.Selector?.SingleSelectedThing as Pawn;
            return pawn == null ? null : AgentColony.Current?.SessionOf(pawn);
        }

        public static int VisibleTabs(IInspectPane pane)
        {
            if (pane?.CurTabs == null) return 0;
            int n = 0;
            foreach (var t in pane.CurTabs)
                if (t.IsVisible) n++;
            return n;
        }

        // Only when the tabs already fill it - a colonist's five leave the leftmost slot
        // free, and taking that one keeps the pane the size the player is used to.
        [HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.PaneWidthFor))]
        public static class Patch_PaneWidthFor
        {
            static void Postfix(IInspectPane pane, ref float __result)
            {
                if (Selected() == null) return;
                __result = Mathf.Max(__result, TabW * (VisibleTabs(pane) + 1));
            }
        }

        // A postfix, so it lands on top of the strip DoTabs fills there when a tab is
        // open.
        [HarmonyPatch(typeof(InspectPaneUtility), "DoTabs")]
        public static class Patch_DoTabs
        {
            static void Postfix(IInspectPane pane)
            {
                if (SlopLayout.Hidden) return;

                string session = Selected();
                if (session == null) return;

                float x = InspectPaneUtility.PaneWidthFor(pane) - TabW * (VisibleTabs(pane) + 1);
                var rect = new Rect(x, pane.PaneTopY - TabH, TabW, TabH);

                Text.Font = GameFont.Small;
                TooltipHandler.TipRegion(rect,
                    $"Edit '{session}': directory, command, sandbox, or its name.");

                if (!SlopWidgets.Button(rect, "Edit")) return;

                var info = SessionHub.Instance.Get(session);
                if (info != null) Find.WindowStack.Add(new EditSessionDialog(info));
                else Messages.Message($"SlopWorld: no session '{session}' to edit.",
                    MessageTypeDefOf.RejectInput, false);
            }
        }

        // Where the pane already lists what a pawn is up to.
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetInspectString))]
        public static class Patch_GetInspectString
        {
            static void Postfix(Pawn __instance, ref string __result)
            {
                var session = AgentColony.Current?.SessionOf(__instance);
                if (session == null) return;

                var info = SessionHub.Instance.Get(session);
                var state = info?.State ?? AgentState.Down;

                // Replaced, not appended: what vanilla puts here is gender, age and whichever job
                // the pawn is faking, which the pane is stripped of anyway.
                __result = string.IsNullOrEmpty(info?.Dir)
                    ? $"Agent: {state.ToString().ToLower()}"
                    : $"{info.Dir}\nAgent: {state.ToString().ToLower()}";
            }
        }
    }
}
