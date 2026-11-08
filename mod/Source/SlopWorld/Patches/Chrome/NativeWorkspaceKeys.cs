using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Waylith suppresses the entire GameKeys stream when an editable field has focus.
    // Admit only workspace bindings through that stream; text/edit/control events keep
    // their native paths. The normal window dispatch still owns handling and modal gates.
    [HarmonyPatch]
    static class Patch_NativeWorkspaceKeys
    {
        static readonly MethodInfo Captures = AccessTools.PropertyGetter(
            AccessTools.TypeByName("UnityStubs.Internal.TextFocus"),
            "CapturesText"
        );
        static readonly MethodInfo Consumes = AccessTools.Method(
            AccessTools.TypeByName("UnityStubs.Internal.TextFocus"),
            "Consumes"
        );
        static readonly Func<KeyCode, bool> NativeConsumes = AccessTools.MethodDelegate<
            Func<KeyCode, bool>
        >(Consumes);

        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("RimWorldWaylith.Imgui.ImguiDispatch"),
                "DispatchKeyboard"
            );

        internal static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions
        ) =>
            NativeKeyDispatch.Rewrite(
                instructions,
                Captures,
                Consumes,
                AccessTools.Method(typeof(Patch_NativeWorkspaceKeys), nameof(ConsumesGameKey))
            );

        static bool ConsumesGameKey(KeyCode key, EventModifiers modifiers)
        {
            bool consumed = NativeConsumes(key);
            if (!consumed)
                return consumed;
            if (ModOptions.KeyboardCaptureActive)
                return false;
            if (Current.ProgramState != ProgramState.Playing)
                return consumed;
            var e = new Event
            {
                type = EventType.KeyDown,
                keyCode = key,
                modifiers = modifiers,
            };
            return !TerminalInputController.IsWorkspaceKey(e);
        }
    }
}
