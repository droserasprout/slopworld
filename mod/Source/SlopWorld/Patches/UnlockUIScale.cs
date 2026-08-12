using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Every thirty frames ResolutionUtility.Update measures the scaled screen and, when it is
    // under 1024x768, throws the UI scale back to whatever it considers recommended. That is
    // why anything past 1.75x on a 1080p display snaps back half a second after it is set,
    // with the game suggesting the config file instead. Vanilla already exempts one case from
    // the reset - dev mode - so the smallest honest patch takes that exemption always: one
    // call site swapped, and the windowed screen-size bookkeeping Update also does untouched.
    //
    // The instruction is edited in place rather than replaced, so any label or exception
    // block anchored to it survives.
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
