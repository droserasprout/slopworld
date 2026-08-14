using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Keep the bottom gizmo grid one group gap beyond the sidebar, with its button bottoms
    // aligned to the add strip's top. The vanilla vertical reserve is for the main-button bar.
    // `Active` distinguishes `DrawGizmoGridFor` from the architect tab's designator grid.

    [HarmonyPatch(typeof(GizmoGridDrawer), "DrawGizmoGridFor")]
    public static class Patch_GizmoGridFlag
    {
        // True while a DrawGizmoGridFor call is on the stack, meaning the next DrawGizmoGrid
        // call is the bottom-of-screen gizmo grid rather than an architect designator list.
        public static bool Active;

        static void Prefix()
        {
            if (SlopLayout.Shown) Active = true;
        }

        static void Postfix()
        {
            Active = false;
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
            float inset = SlopLayout.LeftInset;
            if (inset <= 0f) return;
            startX = InspectPaneAgent.AgentSelectionActive
                ? inset + SlopWidgets.GapM
                : Mathf.Max(startX, inset + SlopWidgets.GapM);
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var screenHeight = AccessTools.Field(typeof(UI), nameof(UI.screenHeight));
            var gizmoSpacing = AccessTools.Field(typeof(GizmoGridDrawer), "GizmoSpacing");
            var vectorY = AccessTools.Field(typeof(Vector2), nameof(Vector2.y));
            var gridTop = AccessTools.Method(typeof(Patch_GizmoGridShift), nameof(GridTop));

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

            Log.Error("[SlopWorld] GizmoGridDrawer.DrawGizmoGrid changed; action buttons keep the vanilla bottom margin");
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
