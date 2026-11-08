using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SlopWorld
{
    // Rewrite only the native game-key lane, retaining the earlier text-edit lane.
    internal static class NativeKeyDispatch
    {
        internal static IEnumerable<CodeInstruction> Rewrite(
            IEnumerable<CodeInstruction> instructions,
            MethodInfo capturesText,
            MethodInfo consumesKey,
            MethodInfo gameFilter
        )
        {
            int captures = 0,
                consumes = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(capturesText))
                {
                    instruction.opcode = OpCodes.Ldc_I4_0;
                    instruction.operand = null;
                    captures++;
                }
                else if (instruction.Calls(consumesKey) && ++consumes == 2)
                {
                    var modifiers = new CodeInstruction(OpCodes.Ldarg_2);
                    modifiers.MoveLabelsFrom(instruction);
                    modifiers.MoveBlocksFrom(instruction);
                    yield return modifiers;
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = gameFilter;
                }
                yield return instruction;
            }
            if (captures != 1 || consumes != 2)
                throw new InvalidOperationException("Unexpected RimWorld 1.7 keyboard dispatch IL");
        }
    }
}
