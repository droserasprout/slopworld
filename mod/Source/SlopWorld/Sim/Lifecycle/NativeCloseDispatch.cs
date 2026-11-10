using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SlopWorld
{
    // Keep the native flag read, but consult policy before an instance loop can exit.
    internal static class NativeCloseDispatch
    {
        internal static IEnumerable<CodeInstruction> Rewrite(
            IEnumerable<CodeInstruction> instructions,
            MethodInfo getClose,
            MethodInfo guard
        )
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (!instruction.Calls(getClose))
                    continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, guard);
                count++;
            }
            if (count != 1)
                throw new InvalidOperationException("Unexpected RimWorld 1.7 window-close loop IL");
        }
    }
}
