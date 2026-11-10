using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SlopWorld
{
    // Capture the compatibility runtime's accepted events, including OS repeats and
    // layout-resolved text. IMGUI remains responsible for focus, modal and key routing.
    internal static class OrderedKeyboard
    {
        internal static readonly KeyboardEventQueue Events = new KeyboardEventQueue();
        static readonly Dictionary<Key, KeyCode> Codes = new Dictionary<Key, KeyCode>();
        delegate bool GetControl(KeyCode code, out KeyControl control);
        static readonly GetControl FindControl = AccessTools.MethodDelegate<GetControl>(
            AccessTools.Method(typeof(Input), "TryGetKeyControl")
        );

        internal static MethodBase RuntimeMethod(string name) =>
            AccessTools.Method(
                AccessTools.TypeByName("UnityEngine.InputSystem.InputRuntime"),
                name
            );

        internal static EventModifiers Modifiers()
        {
            var keyboard = Keyboard.current;
            EventModifiers mods = 0;
            if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
                mods |= EventModifiers.Shift;
            if (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed)
                mods |= EventModifiers.Control;
            if (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed)
                mods |= EventModifiers.Alt;
            if (keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed)
                mods |= EventModifiers.Command;
            return mods;
        }

        internal static void KeyEvent(Key key, bool pressed, bool repeat)
        {
            if (Codes.Count == 0)
                foreach (KeyCode code in Enum.GetValues<KeyCode>())
                    if (FindControl(code, out var control) && control != null)
                        Codes.TryAdd(control.keyCode, code);
            if (Codes.TryGetValue(key, out var mapped))
                Events.Key(mapped, pressed, repeat, Modifiers());
        }
    }

    [HarmonyPatch]
    static class Patch_OrderedKeyChange
    {
        static MethodBase TargetMethod() => OrderedKeyboard.RuntimeMethod("QueueKeyChange");

        static void Prefix(Key key, bool isPressed, out bool __state)
        {
            var control = Keyboard.current[key];
            __state = control != null && control.isPressed != isPressed;
        }

        static void Postfix(Key key, bool isPressed, bool __state)
        {
            if (__state)
                OrderedKeyboard.KeyEvent(key, isPressed, false);
        }
    }

    [HarmonyPatch]
    static class Patch_OrderedKeyRepeat
    {
        static MethodBase TargetMethod() => OrderedKeyboard.RuntimeMethod("QueueKeyRepeat");

        static void Postfix(Key key)
        {
            if (Keyboard.current[key]?.isPressed == true)
                OrderedKeyboard.KeyEvent(key, true, true);
        }
    }

    [HarmonyPatch]
    static class Patch_OrderedText
    {
        static MethodBase TargetMethod() => OrderedKeyboard.RuntimeMethod("QueueTextInput");

        static void Postfix(char character) =>
            OrderedKeyboard.Events.Text(character, OrderedKeyboard.Modifiers());
    }

    [HarmonyPatch]
    static class Patch_OrderedKeyboardReset
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return OrderedKeyboard.RuntimeMethod("BeginFrame");
            yield return OrderedKeyboard.RuntimeMethod("ResetAllInput");
        }

        static void Prefix() => OrderedKeyboard.Events.Clear();
    }
}
