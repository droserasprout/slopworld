using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // ResolutionUtility.Update resets scales when the scaled screen is below 1024x768. Make
    // vanilla's DevMode exemption unconditional, replacing only that getter so the rest of
    // the method and its instruction anchors remain intact.
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
                Log.Error("[SlopWorld] ResolutionUtility.Update changed; UI scale stays capped");
            return code;
        }
    }
}
