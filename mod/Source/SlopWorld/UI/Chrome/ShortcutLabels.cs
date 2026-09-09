using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The same compact names are used in the key-binding page and in the help window. The
    // KeyCode names Unity gives Alpha1 and UpArrow are implementation names, not labels a
    // player should have to translate while looking for a shortcut.
    static class ShortcutLabels
    {
        public static string Binding(KeyBindingDef binding)
        {
            var data = KeyPrefs.KeyPrefsData;
            if (binding == null || data == null) return "(none)";

            string a = Key(data.GetBoundKeyCode(binding, KeyPrefs.BindingSlot.A));
            string b = Key(data.GetBoundKeyCode(binding, KeyPrefs.BindingSlot.B));
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return "(none)";
            if (string.IsNullOrEmpty(b)) return a;
            if (string.IsNullOrEmpty(a)) return b;
            return $"{a} / {b}";
        }

        public static string Key(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.None: return "";
                case KeyCode.Return: return "Enter";
                case KeyCode.KeypadEnter: return "Enter";
                case KeyCode.Escape: return "Esc";
                case KeyCode.Backspace: return "Backspace";
                case KeyCode.Delete: return "Delete";
                case KeyCode.Insert: return "Insert";
                case KeyCode.PageUp: return "PgUp";
                case KeyCode.PageDown: return "PgDn";
                case KeyCode.UpArrow: return "Up";
                case KeyCode.DownArrow: return "Down";
                case KeyCode.LeftArrow: return "Left";
                case KeyCode.RightArrow: return "Right";
                case KeyCode.Tab: return "Tab";
                case KeyCode.Space: return "Space";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                case KeyCode.Slash: return "/";
                case KeyCode.Semicolon: return ";";
                case KeyCode.LeftShift:
                case KeyCode.RightShift: return "Shift";
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt: return "Alt";
                case KeyCode.LeftControl:
                case KeyCode.RightControl: return "Ctrl";
                case KeyCode.LeftCommand:
                case KeyCode.RightCommand: return "Cmd";
                case KeyCode.LeftWindows:
                case KeyCode.RightWindows: return "Win";
                case KeyCode.Alpha0: return "0";
                case KeyCode.Alpha1: return "1";
                case KeyCode.Alpha2: return "2";
                case KeyCode.Alpha3: return "3";
                case KeyCode.Alpha4: return "4";
                case KeyCode.Alpha5: return "5";
                case KeyCode.Alpha6: return "6";
                case KeyCode.Alpha7: return "7";
                case KeyCode.Alpha8: return "8";
                case KeyCode.Alpha9: return "9";
                case KeyCode.Keypad0: return "Num 0";
                case KeyCode.Keypad1: return "Num 1";
                case KeyCode.Keypad2: return "Num 2";
                case KeyCode.Keypad3: return "Num 3";
                case KeyCode.Keypad4: return "Num 4";
                case KeyCode.Keypad5: return "Num 5";
                case KeyCode.Keypad6: return "Num 6";
                case KeyCode.Keypad7: return "Num 7";
                case KeyCode.Keypad8: return "Num 8";
                case KeyCode.Keypad9: return "Num 9";
                default: return key.ToString();
            }
        }
    }
}
