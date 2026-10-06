using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Handles terminal wheel input, mouse reports, and text selection.
    sealed partial class TerminalInputController
    {
        internal void HandleWheel(Rect body, Event e)
        {
            if (!body.Contains(e.mousePosition) || (e.delta.x == 0f && e.delta.y == 0f)) return;

            var live = SessionHub.Instance.Screen(_panel.SessionName);
            bool editor = IsEditorSession();
            var session = SessionHub.Instance.Get(_panel.SessionName);
            bool reader = session != null && (session.Intent == "view" || session.Intent == "diff");

            // Keep wheel input in history while the user reads old output.
            // Do not send it to the live app.
            if (_panel.ScrollOffset > 0)
            {
                // SmoothScroll updates the local pixel position after this handler returns.
                // The draw pass requests the next integer snapshot. This keeps wheel input responsive.
                return;
            }

            // Send batched wheel reports to the pointer cell. `step` sets the count for one tmux write.
            if (live != null && live.AppMouse)
            {
                if (e.delta.y == 0f) return;
                int step = reader ? 1 : Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(e.delta.y)), 1, 5);
                _panel.ClearSelection();
                var cell = _panel.CellAt(body, e.mousePosition);
                string act = e.delta.y < 0f ? "wheelup" : "wheeldown";
                SessionHub.Instance.Terminal.SendMouse(_panel.SessionName, act, 0, cell.x, cell.y, step);
                e.Use();
                return;
            }

            // Translate wheel input to arrow keys for alternate-screen apps without mouse reporting.
            // An editor task can start before the client receives its alternate-screen event.
            // During that frame, arrow keys scroll micro and are safe while the shell hands control to it.
            if (editor || (live != null && live.AltScreen))
            {
                // Use the dominant axis to ignore slight touchpad drift. Pagers receive normal arrow keys.
                bool horizontal = Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y);
                float delta = horizontal ? e.delta.x : e.delta.y;
                int step = reader ? 1 : Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(delta)), 1, 5);
                string key = horizontal ? (delta < 0f ? "Left" : "Right")
                    : (delta < 0f ? "Up" : "Down");
                _panel.ClearSelection();
                var keys = new string[step];
                for (int k = 0; k < step; k++) keys[k] = key;
                SessionHub.Instance.Terminal.SendKeys(_panel.SessionName, keys, false);
                e.Use();
                return;
            }

            // SmoothScroll keeps fractional X11 wheel input. The draw pass requests history snapshots without blocking local motion.
        }

        bool IsEditorSession()
        {
            var info = SessionHub.Instance.Get(_panel.SessionName);
            if (info == null) return false;
            if (Pager.IsEditorCommand(info.Cmd)) return true;
            return info.Ephemeral &&
                (info.Name ?? "").StartsWith("edit-", System.StringComparison.Ordinal);
        }

        internal void HandleMouse(Rect body, Event e)
        {
            bool observedClick = false;
            try
            {
                // Do not start terminal input when another control owns the drag, such as the sidebar, scrollbar, or divider.
                if (MouseType(e) == EventType.MouseDown && GUIUtility.hotControl != 0) return;
                // Continue a forwarded press even if Shift or app mode changes.
                if (_panel.OwnsForwardedMouse(e))
                {
                    if (_panel.HandleMouseForward(body, e)) return;
                    _panel.SelectionInput.Handle(body, e);
                    return;
                }
                // Handle the scrollbar before terminal input. It covers the terminal's rightmost cells.
                if (_panel.HandleHistoryBarInput(body, e)) return;

                // Keep the pane menu available in every mode, including full-screen TUIs.
                if (IsContextMenuEvent(e))
                {
                    if (IsMouseDownInside(body, e))
                    {
                        string url = _panel.LinkUnder(body, e.mousePosition);
                        _panel.OpenMenu(url);
                    }
                    e.Use();
                    return;
                }

                // Middle-click pastes the PRIMARY selection. Handle it before app mouse reporting.
                if (IsPrimaryPasteEvent(body, e))
                {
                    _panel.JumpToLive();
                    _panel.PastePrimarySelection();
                    e.Use();
                    return;
                }

                // Open URLs with Ctrl+click before app mouse reporting. A TUI may consume the click.
                if (IsLinkClick(body, e))
                {
                    TerminalPanel.OpenUrl(_panel.LinkUnder(body, e.mousePosition));
                    e.Use();
                    return;
                }

                if (IsPathClick(body, e, out string path, out int line))
                {
                    _panel.OpenPathMenu(path, line);
                    e.Use();
                    return;
                }

                var live = SessionHub.Instance.Screen(_panel.SessionName);
                // Let mouse-reporting apps own every click in a sequence. Shift and
                // history clicks retain local selection, including word and line gestures.
                if (ShouldForwardMouse(live, e) && _panel.HandleMouseForward(body, e)) return;

                observedClick = MouseType(e) == EventType.MouseDown && e.button == 0 &&
                    body.Contains(e.mousePosition);
                if (_panel.SelectionInput.TryHandleMultiClick(body, e))
                {
                    return;
                }

                if (!IsPrimaryMouse(e)) return;

                _panel.SelectionInput.Handle(body, e);
            }
            finally
            {
                if (!observedClick && MouseType(e) == EventType.MouseDown)
                    _panel.SelectionInput.ResetClicks();
            }
        }

        static bool IsContextMenuEvent(Event e) => e.button == 1;

        static bool IsPrimaryPasteEvent(Rect body, Event e) =>
            MouseType(e) == EventType.MouseDown && e.button == 2 && body.Contains(e.mousePosition);

        static bool IsMouseDownInside(Rect body, Event e) =>
            MouseType(e) == EventType.MouseDown && body.Contains(e.mousePosition);

        bool IsLinkClick(Rect body, Event e) =>
            MouseType(e) == EventType.MouseDown && e.button == 0 && ControlHeld(e) &&
            body.Contains(e.mousePosition) && _panel.LinkUnder(body, e.mousePosition) != null;

        bool IsPathClick(Rect body, Event e, out string path, out int line)
        {
            path = null;
            line = 0;
            if (MouseType(e) != EventType.MouseDown || e.button != 0 || !ControlHeld(e) ||
                !body.Contains(e.mousePosition)) return false;
            path = _panel.PathUnder(body, e.mousePosition, out line);
            return path != null;
        }

        bool ShouldForwardMouse(ScreenBuf live, Event e) =>
            MouseType(e) == EventType.MouseDown && _panel.ScrollOffset == 0 &&
            live != null && live.AppMouse && !e.shift;

        static bool IsPrimaryMouse(Event e) => e.button == 0;

        // WindowStack can mark a mouse event Used before the window receives it. Read its original type to keep clicks and selection working.
        internal static EventType MouseType(Event e) =>
            UiEvent.RawType(e);

        static bool ControlHeld(Event e) =>
            e.control || e.command || Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.RightControl);


    }
}
