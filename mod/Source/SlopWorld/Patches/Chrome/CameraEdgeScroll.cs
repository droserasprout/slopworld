using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Camera-only fullscreen semantics. LinuxGameWindow owns the real display mode;
    // EcoMapInput and the game retain their input, preference, and UI restrictions.
    [HarmonyPatch]
    static class CameraEdgeScroll
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CameraDriver), "CalculateCurInputDollyVect");
            yield return AccessTools.PropertyGetter(
                typeof(CameraDriver),
                "ScreenDollyEdgeWidthBottom"
            );
        }

        // EWMH fullscreen leaves Unity windowed. Use the same fullscreen interpretation
        // for eligibility and the narrower bottom edge, without changing global screen state.
        static bool Fullscreen() =>
            Screen.fullScreen
            || (Application.platform == RuntimePlatform.LinuxPlayer && Settings.Fullscreen);

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var getter = AccessTools.PropertyGetter(typeof(Screen), nameof(Screen.fullScreen));
            var matches = code.Where(instruction => instruction.Calls(getter)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Camera fullscreen check changed.");
            matches[0].operand = AccessTools.Method(typeof(CameraEdgeScroll), nameof(Fullscreen));
            return code;
        }
    }
}
