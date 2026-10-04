using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // Native resize-grip sizing shared by UiWindow and opt-in multiline fields.
    [HarmonyPatch(typeof(WindowResizer), nameof(WindowResizer.DoResizeControl))]
    public static class Patch_WindowResizeCorner
    {
        static float CornerSize() =>
            UiAreaResize.DrawingGrip || Find.WindowStack?.currentlyDrawnWindow is UiWindow
                ? UiAreaResize.CornerSize : 24f;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var size = AccessTools.Method(typeof(Patch_WindowResizeCorner), nameof(CornerSize));
            foreach (var instruction in instructions)
            {
                // RimWorld 1.6 constructs one 24px rectangle for both drawing and hit tests.
                // Changing these instructions in place preserves branch labels.
                if (instruction.opcode == OpCodes.Ldc_R4 && Equals(instruction.operand, 24f))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = size;
                }
                yield return instruction;
            }
        }
    }
}
