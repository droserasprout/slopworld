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

        // Text arrives as UTF-16 character events. Flush after the whole native batch
        // so surrogate pairs stay together, without waiting for a rendered frame.
        // Named keys already flush earlier text before sending their own input.
        static void Postfix() => WorkspaceWindow.FlushTerminalInput();

        static readonly Type DispatchType = AccessTools.TypeByName(
            "RimWorldWaylith.Imgui.ImguiDispatch"
        );
        static readonly MethodInfo RunKey = AccessTools.Method(DispatchType, "RunKeyPass");
        static readonly HashSet<KeyCode> ControlKeys = KeySet("ControlKeys");
        static readonly HashSet<KeyCode> ModifierKeys = KeySet("ModifierKeys");
        static readonly HashSet<KeyCode> GameKeys = KeySet("GameKeys");
        static readonly HashSet<KeyCode> EditKeys = KeySet("EditShortcutKeys");
        static readonly Func<KeyCode, bool> ImeConsumed = AccessTools.MethodDelegate<
            Func<KeyCode, bool>
        >(
            AccessTools.Method(
                AccessTools.TypeByName("RimWorldWaylith.WindowsImeBridge"),
                "Consumed"
            )
        );

        static HashSet<KeyCode> KeySet(string name) =>
            new HashSet<KeyCode>((KeyCode[])AccessTools.Field(DispatchType, name).GetValue(null));

        static bool Prefix(object __instance, Vector2 physMouse, List<char> ____typedChars)
        {
            var batch = OrderedKeyboard.Events.Drain();
            if (!WorkspaceWindow.HasTerminalKeyboardInput)
                return true;

            // The native dispatcher sorts text before control keys and polls each key
            // only once using end-of-frame modifiers. Replay accepted events in order
            // instead, through its normal GUI passes and existing focus gates.
            ____typedChars.Clear();
            foreach (var input in batch)
            {
                var key = input.Key;
                bool control = ControlKeys.Contains(key);
                bool modifier = ModifierKeys.Contains(key);
                bool game = GameKeys.Contains(key);
                if (input.Character == '\0')
                {
                    if (!control && !modifier && !game)
                        continue;
                    if (input.Repeat && modifier)
                        continue;
                    if (
                        input.Type == EventType.KeyDown
                        && control
                        && OperatingSystem.IsWindows()
                        && ImeConsumed(key)
                    )
                        continue;
                    if (input.Type == EventType.KeyDown && !control && !modifier)
                    {
                        bool edit =
                            (input.Modifiers & EventModifiers.Control) != 0
                            && EditKeys.Contains(key)
                            && NativeConsumes(key);
                        if (!edit && ConsumesGameKey(key, input.Modifiers))
                            continue;
                    }
                }
                RunKey.Invoke(
                    __instance,
                    new object[] { key, input.Character, physMouse, input.Modifiers, input.Type }
                );
            }
            return false;
        }

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
