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
        const float SessionPaneW = 432f;

        // The session behind the current selection. A selected Thing is authoritative for
        // the pane; when the selector is empty the mod-owned session selection stands for a
        // ghost and for a session reached by comma/dot.
        public static string Selected()
        {
            // Find.Selector reaches through Find.MapUI, which casts the UI root to
            // UIRoot_Play; on the entry root that throws, and an exception here escapes
            // UIRootOnGUI before the window stack is drawn, leaving the screen blank.
            if (Current.ProgramState != ProgramState.Playing) return null;

            var selected = Find.Selector?.SingleSelectedThing;
            if (selected != null)
            {
                var pawn = selected as Pawn;
                return pawn == null ? null : AgentColony.Current?.SessionOf(pawn);
            }

            return SessionSelectable.HasCurrent ? SessionSelectable.Current : null;
        }

        public static SessionInfo Info(string session) =>
            session == null ? null : SessionHub.Instance.Get(session);

        public static void DrawSessionContents(Rect rect, string session)
        {
            var info = Info(session);
            if (info == null) return;

            float y = 0f;
            Text.Font = GameFont.Small;
            GUI.color = SlopWidgets.Lead;
            Widgets.Label(new Rect(0f, y, rect.width, SlopWidgets.LineH), session);
            GUI.color = TerminalWindow.StateColor(info.State);
            Widgets.Label(new Rect(0f, y + SlopWidgets.LineH, rect.width,
                SlopWidgets.LineH), info.State.ToString().ToLower());
            GUI.color = SlopWidgets.Dim;
            y += SlopWidgets.LineH * 2f + SlopWidgets.GapXS;

            string ground = string.IsNullOrEmpty(info.Dir) ? "(no directory)" : info.Dir;
            Widgets.Label(new Rect(0f, y, rect.width, SlopWidgets.LineH), ground);
            y += SlopWidgets.LineH;

            if (!string.IsNullOrEmpty(info.Title))
            {
                GUI.color = SlopWidgets.Name;
                Widgets.Label(new Rect(0f, y, rect.width, SlopWidgets.LineH), info.Title);
            }
            GUI.color = Color.white;
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
                // With no Thing, vanilla has no tabs and reports one 72px slot. The
                // session body still needs the ordinary inspect width, including room for
                // the Edit button and a directory line.
                __result = Mathf.Max(__result, SessionPaneW,
                    TabW * (VisibleTabs(pane) + 1));
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

        // Give the inspect window a body even when Selector has no Thing. The vanilla
        // window remains the host for this pane; only its selection predicates and body are
        // replaced for the current session, so the existing chrome and gizmo placement stay
        // in one coordinate system.
        [HarmonyPatch(typeof(MainTabWindow_Inspect), "AnythingSelected", MethodType.Getter)]
        public static class Patch_AnythingSelected
        {
            static void Postfix(ref bool __result) =>
                __result = __result || Selected() != null;
        }

        [HarmonyPatch(typeof(MainTabWindow_Inspect), "ShouldShowPaneContents", MethodType.Getter)]
        public static class Patch_ShouldShowPaneContents
        {
            static void Postfix(ref bool __result) =>
                __result = __result || Selected() != null;
        }

        [HarmonyPatch(typeof(MainTabWindow_Inspect), nameof(MainTabWindow_Inspect.GetLabel))]
        public static class Patch_GetLabel
        {
            static void Postfix(Rect rect, ref string __result)
            {
                var session = Selected();
                if (session != null) __result = session;
            }
        }

        [HarmonyPatch(typeof(MainTabWindow_Inspect), nameof(MainTabWindow_Inspect.DoPaneContents))]
        public static class Patch_DoPaneContents
        {
            static bool Prefix(Rect rect)
            {
                var session = Selected();
                if (session == null) return true;
                DrawSessionContents(rect, session);
                return false;
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
