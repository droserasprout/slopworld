using UnityEngine;

namespace SlopWorld
{
    // Terminal key encoding. Workspace shortcuts are consumed before this stage.
    sealed partial class TerminalInputController
    {
        internal static string MapKey(Event e, bool altScreen)
        {
            // Use tmux modifier names. Shifted navigation belongs to alternate-screen apps;
            // Shift on function keys only bypasses workspace shortcuts. Tab uses its dedicated BTab name.
            string mod = "";
            if (e.control) mod += "C-";
            if (e.alt) mod += "M-";
            bool functionKey = e.keyCode >= KeyCode.F1 && e.keyCode <= KeyCode.F12;
            if (e.shift && altScreen && !functionKey) mod += "S-";

            switch (e.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter: return "Enter";
                case KeyCode.Escape: return "Escape";
                case KeyCode.Backspace: return mod + "BSpace";
                case KeyCode.Tab:
                    // BTab already includes Shift; retain the independent Ctrl/Alt modifiers.
                    return (e.control ? "C-" : "") + (e.alt ? "M-" : "") +
                        (e.shift ? "BTab" : "Tab");
                case KeyCode.UpArrow: return mod + "Up";
                case KeyCode.DownArrow: return mod + "Down";
                case KeyCode.LeftArrow: return mod + "Left";
                case KeyCode.RightArrow: return mod + "Right";
                case KeyCode.Home: return mod + "Home";
                case KeyCode.End: return mod + "End";
                case KeyCode.PageUp: return mod + "PPage";
                case KeyCode.PageDown: return mod + "NPage";
                case KeyCode.Delete: return mod + "DC";
                case KeyCode.Insert: return mod + "IC";
                case KeyCode.F1: return mod + "F1";
                case KeyCode.F2: return mod + "F2";
                case KeyCode.F3: return mod + "F3";
                case KeyCode.F4: return mod + "F4";
                case KeyCode.F5: return mod + "F5";
                case KeyCode.F6: return mod + "F6";
                case KeyCode.F7: return mod + "F7";
                case KeyCode.F8: return mod + "F8";
                case KeyCode.F9: return mod + "F9";
                case KeyCode.F10: return mod + "F10";
                case KeyCode.F11: return mod + "F11";
                case KeyCode.F12: return mod + "F12";
            }

            // The caller handles Ctrl+V as paste. Do not forward it as a key.
            if (e.control && e.keyCode == KeyCode.V) return null;

            if (e.control)
            {
                switch (e.keyCode)
                {
                    case KeyCode.Space:
                    case KeyCode.At: return "C-@";
                    case KeyCode.LeftBracket: return "C-[";
                    case KeyCode.Backslash: return "C-\\";
                    case KeyCode.RightBracket: return "C-]";
                    case KeyCode.Caret: return "C-^";
                    case KeyCode.Underscore: return "C-_";
                    case KeyCode.Alpha2: if (e.shift) return "C-@"; break;
                    case KeyCode.Alpha6: if (e.shift) return "C-^"; break;
                    case KeyCode.Minus: if (e.shift) return "C-_"; break;
                }
            }

            if (e.keyCode >= KeyCode.A && e.keyCode <= KeyCode.Z)
            {
                char c = (char)('a' + (e.keyCode - KeyCode.A));
                if (e.control) return "C-" + c;
                if (e.alt) return "M-" + c;
            }

            return null;
        }

    }
}
