using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // ResolutionUtility.Update resets UI scale when the scaled screen is smaller than 1024 by 768.
    // Always apply its DevMode exemption by replacing that getter. Retain the other instructions.
    [HarmonyPatch(typeof(ResolutionUtility), nameof(ResolutionUtility.Update))]
    public static class Patch_ResolutionUpdate_UIScale
    {
        static bool Exempt() => true;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var devMode = AccessTools.PropertyGetter(typeof(Prefs), nameof(Prefs.DevMode));
            var exempt = AccessTools.Method(typeof(Patch_ResolutionUpdate_UIScale),
                nameof(Exempt));
            bool swapped = false;

            for (int i = 0; i < code.Count; i++)
            {
                if (!code[i].Calls(devMode)) continue;
                code[i].opcode = OpCodes.Call;
                code[i].operand = exempt;
                swapped = true;
                break;
            }

            if (!swapped)
                Log.Error("[SlopWorld] ResolutionUtility.Update changed. UI scale stays capped.");
            return code;
        }
    }
}
