using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Place the bottom gizmo grid beside the sidebar and above the add bar.
    // Active distinguishes this grid from the Architect designator grid.

    [HarmonyPatch(typeof(MapGizmoUtility), "MapUIOnGUI")]
    public static class Patch_GizmoGridFlag
    {
        // Only map gizmos use the sidebar bottom margin; nested scopes restore their caller.
        public static bool Active;

        static void Prefix(out bool __state)
        {
            __state = Active;
            Active = UiLayout.Shown;
        }

        static void Finalizer(bool __state)
        {
            Active = __state;
        }
    }

    [HarmonyPatch(typeof(GizmoGridDrawer), "DrawGizmoGrid")]
    public static class Patch_GizmoGridShift
    {
        static float GridTop(float screenHeight, float spacingY)
        {
            if (Patch_GizmoGridFlag.Active)
                return AgentSidebar.AddBar.y - 75f;

            return screenHeight - 35f - spacingY - 75f;
        }

        static void Prefix(ref float startX)
        {
            if (!Patch_GizmoGridFlag.Active) return;
            var content = WorkspaceLayout.Current.Content;
            float start = content.x + UiTheme.GapM;
            if (content.width >= UI.screenWidth - 0.01f) return;
            startX = InspectPaneAgent.AgentSelectionActive
                ? start : Mathf.Max(startX, start);
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var screenHeight = AccessTools.Field(typeof(UI), nameof(UI.screenHeight));
            var gizmoSpacing = AccessTools.Field(typeof(GizmoGridDrawer), "GizmoSpacing");
            var vectorY = AccessTools.Field(typeof(Vector2), nameof(Vector2.y));
            var gridTop = AccessTools.Method(typeof(Patch_GizmoGridShift), nameof(GridTop));

            // Vanilla computes (float)(UI.screenHeight - 35) - GizmoSpacing.y - 75f.
            // Preserve the expression's entry labels so branches still enter the replacement.
            for (int i = 0; i + 8 < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldsfld || !Equals(code[i].operand, screenHeight)
                    || !IsInt(code[i + 1], 35)
                    || code[i + 2].opcode != OpCodes.Sub
                    || code[i + 3].opcode != OpCodes.Conv_R4
                    || code[i + 4].opcode != OpCodes.Ldsflda
                    || !Equals(code[i + 4].operand, gizmoSpacing)
                    || code[i + 5].opcode != OpCodes.Ldfld
                    || !Equals(code[i + 5].operand, vectorY)
                    || code[i + 6].opcode != OpCodes.Sub
                    || !IsFloat(code[i + 7], 75f)
                    || code[i + 8].opcode != OpCodes.Sub)
                    continue;

                var replacement = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldsfld, screenHeight),
                    new CodeInstruction(OpCodes.Conv_R4),
                    new CodeInstruction(OpCodes.Ldsflda, gizmoSpacing),
                    new CodeInstruction(OpCodes.Ldfld, vectorY),
                    new CodeInstruction(OpCodes.Call, gridTop),
                };
                replacement[0].labels.AddRange(code[i].labels);
                code.RemoveRange(i, 9);
                code.InsertRange(i, replacement);
                return code;
            }

            Log.Error("[SlopWorld] GizmoGridDrawer.DrawGizmoGrid changed. Action buttons keep the vanilla bottom margin.");
            return code;
        }

        static bool IsInt(CodeInstruction instruction, int value) =>
            (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte b && b == value)
            || (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int i && i == value);

        static bool IsFloat(CodeInstruction instruction, float value) =>
            instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float f
            && Mathf.Abs(f - value) < 0.001f;
    }
}
