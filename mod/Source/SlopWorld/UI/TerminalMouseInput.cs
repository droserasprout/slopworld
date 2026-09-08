using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Terminal mouse-wheel, mouse-reporting, and selection input policy.
    sealed partial class TerminalInputController
    {
        internal void HandleWheel(Rect body, Event e)
        {
            if (!body.Contains(e.mousePosition) || (e.delta.x == 0f && e.delta.y == 0f)) return;

            var live = SessionHub.Instance.Screen(_window.SessionName);
            bool editor = IsEditorSession();

            // Already in scrollback: stay there, whatever the live app is doing.
            // The app mode check below would otherwise hijack the wheel and send it
            // into the live app while the user is reading historical output.
            if (_window.ScrollOffset > 0)
            {
                // SmoothScroll owns the event after this handler returns. It advances the
                // local pixel position immediately and requests the next integer snapshot
                // from the draw pass, so history never waits on a wheel round trip.
                return;
            }

            // App wants the mouse: forward wheel reports at the pointer cell. Batched - `step`
            // is one tmux write, not one tmux process per scrolled line.
            if (live != null && live.AppMouse)
            {
                if (e.delta.y == 0f) return;
                int step = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(e.delta.y)), 1, 5);
                _window.ClearSelection();
                var cell = _window.CellAt(body, e.mousePosition);
                string act = e.delta.y < 0f ? "wheelup" : "wheeldown";
                SessionHub.Instance.SendMouse(_window.SessionName, act, 0, cell.x, cell.y, step);
                e.Use();
                return;
            }

            // Alt-screen app with no mouse (less, man, git log): the terminal convention is
            // to translate the wheel to arrow keys.
            // The first frame after an editor errand starts can still be the shell frame:
            // the command has been launched, but the screen event carrying AltScreen has not
            // reached the client yet. Keep that race from turning the first wheel into the
            // terminal's own scrollback; arrows are micro's native scroll path and are harmless
            // while the shell is handing control to it.
            if (editor || (live != null && live.AltScreen))
            {
                // Use the dominant axis so slight touchpad drift does not mix vertical
                // movement into a sideways gesture. Pagers receive their normal arrow keys.
                bool horizontal = Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y);
                float delta = horizontal ? e.delta.x : e.delta.y;
                int step = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(delta)), 1, 5);
                string key = horizontal ? (delta < 0f ? "Left" : "Right")
                    : (delta < 0f ? "Up" : "Down");
                _window.ClearSelection();
                var keys = new string[step];
                for (int k = 0; k < step; k++) keys[k] = key;
                SessionHub.Instance.SendKeys(_window.SessionName, keys, false);
                e.Use();
                return;
            }

            // Walk our own scrollback view.
            // SmoothScroll owns the event after this handler returns. Its fractional X11
            // sample preserves touchpad movement; the draw pass converts it to history
            // snapshot requests without blocking the local motion.
        }

        bool IsEditorSession()
        {
            var info = SessionHub.Instance.Get(_window.SessionName);
            if (info == null) return false;
            if (Pager.IsEditorCommand(info.Cmd)) return true;
            return info.Ephemeral &&
                (info.Name ?? "").StartsWith("edit-", System.StringComparison.Ordinal);
        }

        internal void HandleMouse(Rect body, Event e)
        {
            // A forwarded press owns its continuation even if Shift or app mode changes.
            if (_window.OwnsForwardedMouse(e))
            {
                if (_window.HandleMouseForward(body, e)) return;
                _window.SelectionInput.Handle(body, e);
                return;
            }
            // The scrollbar sits over the terminal's rightmost cells. Give it first refusal
            // so a click or drag there cannot start a text selection underneath it.
            if (_window.HandleHistoryBarInput(body, e)) return;

            // The pane's own menu is reachable in every mode, including a full-screen TUI.
            if (IsContextMenuEvent(e))
            {
                if (IsMouseDownInside(body, e))
                {
                    string url = _window.LinkUnder(body, e.mousePosition);
                    int line = 0;
                    string menuPath = ControlHeld(e)
                        ? _window.PathUnder(body, e.mousePosition, out line) : null;
                    if (url != null || !IsRelativePath(menuPath))
                    {
                        menuPath = null;
                        line = 0;
                    }
                    _window.OpenMenu(url, menuPath, line);
                }
                e.Use();
                return;
            }

            // Terminal middle-click is the conventional paste gesture. Handle it before app
            // mouse reporting: the primary selection belongs to the terminal even while an
            // alternate-screen application has enabled mouse mode.
            if (IsPrimaryPasteEvent(body, e))
            {
                _window.JumpToLive();
                _window.PastePrimarySelection();
                e.Use();
                return;
            }

            // A URL printed inside a TUI is over something that wants the mouse as often as
            // not, so Ctrl+click takes precedence over app mouse reporting.
            if (IsLinkClick(body, e))
            {
                TerminalWindow.OpenUrl(_window.LinkUnder(body, e.mousePosition));
                e.Use();
                return;
            }

            if (IsPathClick(body, e, out string path))
            {
                var session = SessionHub.Instance.Get(_window.SessionName);
                if (session != null && FilesView.FocusPath(session.Project, path))
                {
                    e.Use();
                    return;
                }
            }

            // Multi-click selection is the terminal's gesture even when the app reports clicks.
            if (_window.SelectionInput.TryHandleMultiClick(body, e))
            {
                return;
            }

            var live = SessionHub.Instance.Screen(_window.SessionName);
            // Shift forces our own selection, like a real terminal.
            if (ShouldForwardMouse(live, e) && _window.HandleMouseForward(body, e)) return;
            if (!IsPrimaryMouse(e)) return;

            _window.SelectionInput.Handle(body, e);
        }

        static bool IsContextMenuEvent(Event e) => e.button == 1;

        static bool IsPrimaryPasteEvent(Rect body, Event e) =>
            MouseType(e) == EventType.MouseDown && e.button == 2 && body.Contains(e.mousePosition);

        static bool IsMouseDownInside(Rect body, Event e) =>
            MouseType(e) == EventType.MouseDown && body.Contains(e.mousePosition);

        bool IsLinkClick(Rect body, Event e) =>
            MouseType(e) == EventType.MouseDown && e.button == 0 && ControlHeld(e) &&
            body.Contains(e.mousePosition) && _window.LinkUnder(body, e.mousePosition) != null;

        bool IsPathClick(Rect body, Event e, out string path)
        {
            path = null;
            if (MouseType(e) != EventType.MouseDown || e.button != 0 || !ControlHeld(e) ||
                !body.Contains(e.mousePosition)) return false;
            int line;
            path = _window.PathUnder(body, e.mousePosition, out line);
            return path != null;
        }

        static bool IsRelativePath(string path) => !string.IsNullOrEmpty(path) && path[0] != '/';

        bool ShouldForwardMouse(ScreenBuf live, Event e) =>
            MouseType(e) == EventType.MouseDown && _window.ScrollOffset == 0 &&
            live != null && live.AppMouse && !e.shift;

        static bool IsPrimaryMouse(Event e) => e.button == 0;

        // A window can receive a mouse event after WindowStack has marked it Used. Keep the
        // original type for all terminal gesture dispatch; otherwise Ctrl+clicks (and ordinary
        // selection presses) disappear before the pane sees them.
        internal static EventType MouseType(Event e) =>
            UiEvent.RawType(e);

        static bool ControlHeld(Event e) =>
            e.control || e.command || Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.RightControl);


        static string MapKey(Event e, bool altScreen)
        {
            // Use tmux's modifier names; Shift is forwarded only on the alt screen because
            // shells do not define the corresponding xterm sequences.
            string mod = "";
            if (e.control) mod += "C-";
            if (e.alt) mod += "M-";
            if (e.shift && altScreen) mod += "S-";

            switch (e.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter: return "Enter";
                case KeyCode.Escape: return "Escape";
                case KeyCode.Backspace: return "BSpace";
                case KeyCode.Tab: return e.shift ? "BTab" : "Tab";
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
                case KeyCode.F1: return "F1";
                case KeyCode.F2: return "F2";
                case KeyCode.F3: return "F3";
                case KeyCode.F4: return "F4";
                case KeyCode.F5: return "F5";
                case KeyCode.F6: return "F6";
                case KeyCode.F7: return "F7";
                case KeyCode.F8: return "F8";
                case KeyCode.F9: return "F9";
                case KeyCode.F10: return "F10";
                case KeyCode.F11: return "F11";
                case KeyCode.F12: return "F12";
            }

            // Ctrl+V is a paste, handled by the caller, not a key to forward.
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
