using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Handles terminal key and mouse input. The panel supplies layout, rendering, and selection services.
    sealed partial class TerminalInputController
    {
        readonly TerminalPanel _panel;
        readonly Dictionary<KeyCode, System.Func<Event, bool>> _local;
        readonly Dictionary<KeyCode, System.Func<Event, bool>> _terminal;
        public TerminalInputController(TerminalPanel panel)
        {
            _panel = panel;
            _local = new Dictionary<KeyCode, System.Func<Event, bool>>
            {
                { KeyCode.Escape, HandleEscapeKey },
                { KeyCode.Return, HandleReturnKey },
                { KeyCode.KeypadEnter, HandleReturnKey },
                { KeyCode.PageUp, HandleHistoryKey },
                { KeyCode.PageDown, HandleHistoryKey },
            };
            _terminal = new Dictionary<KeyCode, System.Func<Event, bool>>
            {
                { KeyCode.Escape, ForwardMappedKey },
                { KeyCode.Return, ForwardMappedKey },
                { KeyCode.KeypadEnter, ForwardMappedKey },
                { KeyCode.Backspace, ForwardMappedKey },
                { KeyCode.Tab, ForwardMappedKey },
                { KeyCode.UpArrow, ForwardMappedKey },
                { KeyCode.DownArrow, ForwardMappedKey },
                { KeyCode.LeftArrow, ForwardMappedKey },
                { KeyCode.RightArrow, ForwardMappedKey },
                { KeyCode.Home, ForwardMappedKey },
                { KeyCode.End, ForwardMappedKey },
                { KeyCode.PageUp, ForwardMappedKey },
                { KeyCode.PageDown, ForwardMappedKey },
                { KeyCode.Delete, ForwardMappedKey },
                { KeyCode.Insert, ForwardMappedKey },
                { KeyCode.F1, ForwardMappedKey },
                { KeyCode.F2, ForwardMappedKey },
                { KeyCode.F3, ForwardMappedKey },
                { KeyCode.F4, ForwardMappedKey },
                { KeyCode.F5, ForwardMappedKey },
                { KeyCode.F6, ForwardMappedKey },
                { KeyCode.F7, ForwardMappedKey },
                { KeyCode.F8, ForwardMappedKey },
                { KeyCode.F9, ForwardMappedKey },
                { KeyCode.F10, ForwardMappedKey },
                { KeyCode.F11, ForwardMappedKey },
                { KeyCode.F12, ForwardMappedKey },
                { KeyCode.C, HandleControlC },
                { KeyCode.V, HandleControlV },
                { KeyCode.Semicolon, HandleSemicolonKey },
                { KeyCode.Colon, HandleSemicolonKey },
                { KeyCode.Space, ForwardMappedKey },
                { KeyCode.At, ForwardMappedKey },
                { KeyCode.LeftBracket, ForwardMappedKey },
                { KeyCode.Backslash, ForwardMappedKey },
                { KeyCode.RightBracket, ForwardMappedKey },
                { KeyCode.Caret, ForwardMappedKey },
                { KeyCode.Underscore, ForwardMappedKey },
                { KeyCode.Alpha2, ForwardMappedKey },
                { KeyCode.Alpha6, ForwardMappedKey },
                { KeyCode.Minus, ForwardMappedKey },
                { KeyCode.A, ForwardMappedKey },
                { KeyCode.B, ForwardMappedKey },
                { KeyCode.D, ForwardMappedKey },
                { KeyCode.E, ForwardMappedKey },
                { KeyCode.F, ForwardMappedKey },
                { KeyCode.G, ForwardMappedKey },
                { KeyCode.H, ForwardMappedKey },
                { KeyCode.I, ForwardMappedKey },
                { KeyCode.J, ForwardMappedKey },
                { KeyCode.K, ForwardMappedKey },
                { KeyCode.L, ForwardMappedKey },
                { KeyCode.M, ForwardMappedKey },
                { KeyCode.N, ForwardMappedKey },
                { KeyCode.O, ForwardMappedKey },
                { KeyCode.P, ForwardMappedKey },
                { KeyCode.Q, ForwardMappedKey },
                { KeyCode.R, ForwardMappedKey },
                { KeyCode.S, ForwardMappedKey },
                { KeyCode.T, ForwardMappedKey },
                { KeyCode.U, ForwardMappedKey },
                { KeyCode.W, ForwardMappedKey },
                { KeyCode.X, ForwardMappedKey },
                { KeyCode.Y, ForwardMappedKey },
                { KeyCode.Z, ForwardMappedKey },
            };
        }

        public void Handle(Rect body)
        {
            var e = Event.current;
            // WindowStack can consume the semicolon before the window draws. Replay that character only.
            // Do not replay other Used events.
            if (e.type == EventType.Used && e.rawType == EventType.KeyDown &&
                (e.character == ';' || IsSemicolonKey(e.keyCode)))
            {
                HandleKey(e);
                return;
            }

            switch (e.type)
            {
                case EventType.ScrollWheel:
                    HandleWheel(body, e);
                    return;
                case EventType.MouseDown:
                case EventType.MouseDrag:
                case EventType.MouseUp:
                    HandleMouse(body, e);
                    return;
                case EventType.Used:
                    if (e.rawType == EventType.MouseDown || e.rawType == EventType.MouseDrag ||
                        e.rawType == EventType.MouseUp)
                        HandleMouse(body, e);
                    return;
                case EventType.KeyDown:
                    HandleKey(e);
                    return;
            }
        }

        public void HandleChrome(Event e)
        {
            if (ShortcutHelpWindow.HandleContentKey(e)) return;
            if (e.type != EventType.KeyDown) return;

            // A pending Keyboard-page binding captures the next key, including keys used by the sidebar or terminal.
            // Escape cancels capture in the page.
            if (ModOptions.KeyboardCaptureActive) return;

            if (HandleFunctionKey(e)) { e.Use(); return; }

            if (e.keyCode == KeyCode.Escape)
            {
                if (_panel.Content == null && HandleEscapeKey(e)) return;
                _panel.Leave();
                e.Use();
                return;
            }

            if (TryHandleSessionNavigation(e)) return;
        }

        public void CaptureSemicolonInput()
        {
            CaptureSemicolonInputCore();
        }

        bool TryLocal(Event e) => Try(_local, e);
        bool TryTerminal(Event e) => Try(_terminal, e);

        static bool Try(Dictionary<KeyCode, System.Func<Event, bool>> handlers, Event e)
        {
            return handlers.TryGetValue(e.keyCode, out var handler) && handler(e);
        }

        // Handle keys assigned to workspace controls. Read KeyBindingDefs so option-menu changes apply.
        // Send shifted and unbound keys to the agent. Close menus before a workspace transition.
        public static bool HandleFunctionKey(Event e)
        {
            // Send Shift+key to the agent or TUI.
            if (e.shift || e.keyCode == KeyCode.None) return false;

            if (Bound(ModDefOf.SlopCommandPalette, e) && e.control && !e.alt && !e.command)
            {
                UiMenu.CloseAll();
                SearchView.ReleaseFocus();
                CommandPalette.Toggle();
                return true;
            }
            if (SidebarFunction(ModDefOf.SlopSidebarAgents, SidebarTab.Agents, e)) return true;
            if (SidebarFunction(ModDefOf.SlopSidebarFiles, SidebarTab.Files, e)) return true;
            if (SidebarFunction(ModDefOf.SlopSidebarGit, SidebarTab.Git, e)) return true;
            if (SidebarFunction(ModDefOf.SlopSidebarSearch, SidebarTab.Search, e)) return true;
            if (SidebarFunction(ModDefOf.SlopSidebarTasks, SidebarTab.Tasks, e)) return true;
            if (SidebarFunction(ModDefOf.SlopSidebarLibrary, SidebarTab.Library, e)) return true;
            if (Bound(ModDefOf.SlopQuickTerminal, e))
            {
                // Use the same toggle as TerminalHotkeys. Settings views share this window, so F12 must reveal the terminal.
                UiMenu.CloseAll();
                TerminalHotkeys.Toggle();
                return true;
            }
            if (Bound(ModDefOf.SlopToggleFullscreen, e))
            {
                UiMenu.CloseAll();
                WindowMaximizer.Toggle();
                return true;
            }
            return false;
        }

        static bool SidebarFunction(KeyBindingDef binding, SidebarTab tab, Event e)
        {
            if (!Bound(binding, e)) return false;

            UiMenu.CloseAll();
            // Bare F1-F6 selects a tab. Ctrl+F1-F6 returns to its last target. Shift bypasses these tab shortcuts.
            if (e.control && !e.alt && !e.command) AgentSidebar.FocusLast(tab);
            else AgentSidebar.ShowTab(tab);
            return true;
        }

        // Check whether the key matches either binding slot. The caller needs this result after it takes the event.
        static bool Bound(KeyBindingDef def, Event e)
        {
            if (def == null) return false;
            var data = KeyPrefs.KeyPrefsData;
            if (data == null) return false;
            return data.GetBoundKeyCode(def, KeyPrefs.BindingSlot.A) == e.keyCode
                || data.GetBoundKeyCode(def, KeyPrefs.BindingSlot.B) == e.keyCode;
        }

        internal static bool TryTabWalkDirection(Event e, out int dir)
        {
            dir = 0;
            if (e.type != EventType.KeyDown || !e.alt || e.shift || e.control) return false;
            if (e.keyCode == KeyCode.Z) dir = -1;
            else if (e.keyCode == KeyCode.X) dir = 1;
            return dir != 0;
        }

        internal void HandleKey(Event e)
        {
            // Try workspace actions before checking the connection. Bare Escape and Return return false, then reach terminal dispatch.
            if (TryLocal(e)) return;

            // WindowStack consumes KeyDown before game components run. Handle every function key here.
            // Bare F-keys control SlopWorld. Shift+F sends the key to the agent.
            if (HandleFunctionKey(e)) { e.Use(); return; }

            // Handle local session changes before checking the connection. This keeps navigation available during redeploy.
            if (TryHandleSessionNavigation(e)) return;

            // The hub drops sends while offline. Count keys so the status banner reports them.
            if (!SessionHub.Instance.Online)
            {
                if (e.keyCode != KeyCode.None || e.character != '\0')
                {
                    _panel.DroppedKeys++;
                    e.Use();
                }
                return;
            }

            // The daemon queues auto-resume after startup settles. Keep input out of the resume picker.
            // Keep workspace controls available so the user can leave this pane.
            if (_panel.AutoResumePending)
            {
                if (e.keyCode != KeyCode.None || e.character != '\0') e.Use();
                return;
            }

            // Unity reports a semicolon with a spurious modifier on this player. The character event has the keyboard-layout result.
            if (e.character == ';')
            {
                AppendSemicolon();
                e.Use();
                return;
            }

            if (TryTerminal(e)) return;

            // Unity sends printable input in a second event that carries only the character.
            if (e.character != '\0' && e.character != '\n' &&
                e.character != '\r' && e.character != '\t' && !e.control && !e.alt)
            {
                _panel.JumpToLive();
                _panel.Literal.Append(e.character);
                e.Use();
                return;
            }

            if (e.keyCode != KeyCode.None)
                e.Use(); // Keep RimWorld hotkeys from handling this key.
        }

        // Handle local session changes while the daemon is offline. Chrome and pane handlers call this after their own key checks.
        internal bool TryHandleSessionNavigation(Event e)
        {
            int slot = TerminalHotkeys.SlotKey(e);
            if (slot >= 0 && e.alt)
            {
                SwitchToSlot(slot);
                e.Use();
                return true;
            }

            if (TryTabWalkDirection(e, out var dir))
            {
                WalkSession(dir);
                e.Use();
                return true;
            }
            return false;
        }

        internal bool HandleEscapeKey(Event e)
        {
            // Use Shift+Escape to close the pane. Send bare Escape to the agent.
            if (!e.shift) return false;
            _panel.Close();
            e.Use();
            return true;
        }

        internal bool HandleReturnKey(Event e)
        {
            // Send the Kitty sequence for Shift+Enter. Compatible apps can insert a newline instead of submitting.
            if (!e.shift) return false;
            _panel.JumpToLive();
            _panel.Flush();
            SessionHub.Instance.Terminal.SendKeys(_panel.SessionName, new[] { "\u001b[13;2u" }, true);
            e.Use();
            return true;
        }

        internal bool HandleHistoryKey(Event e)
        {
            if (!e.shift || e.control || e.alt ||
                (e.keyCode != KeyCode.PageUp && e.keyCode != KeyCode.PageDown))
                return false;

            var live = SessionHub.Instance.Screen(_panel.SessionName);
            // Let alternate-screen apps handle shifted page keys. On the primary screen, use them to scroll terminal history.
            if (_panel.ScrollOffset == 0 && live != null && live.AltScreen) return false;

            int page = _panel.Rows > 0 ? _panel.Rows : live != null ? live.Rows : 1;
            page = Mathf.Max(1, page);
            bool up = e.keyCode == KeyCode.PageUp;
            bool fromLive = _panel.ScrollOffset <= 0;
            if (up) _panel.ScrollOffset += page;
            else _panel.ScrollOffset = Mathf.Max(0, _panel.ScrollOffset - page);
            _panel.JumpHistoryTo(_panel.ScrollOffset);
            _panel.QueueScroll(up, fromLive);
            e.Use();
            return true;
        }

        internal bool HandleControlC(Event e)
        {
            // Without Ctrl, pass the key to the mapper. For example, Alt+C becomes M-c.
            if (!e.control) return ForwardMappedKey(e);
            // Both Ctrl+C and Ctrl+Shift+C copy selected text. Without a selection, Ctrl+C sends SIGINT.
            if (_panel.HasSelection)
            {
                _panel.CopySelection();
                e.Use();
                return true;
            }
            // Without a selection, Ctrl+Shift+C does nothing. Send bare Ctrl+C to MapKey.
            if (e.shift)
            {
                e.Use();
                return true;
            }
            return ForwardMappedKey(e);
        }

        internal bool HandleControlV(Event e)
        {
            // Without Ctrl, pass the key to the mapper. For example, Alt+V becomes M-v.
            if (!e.control) return ForwardMappedKey(e);
            _panel.JumpToLive();
            _panel.PasteClipboard();
            e.Use();
            return true;
        }

        internal bool HandleSemicolonKey(Event e)
        {
            // Some backends omit the character-only event. Keep the shifted character before this handler consumes the key.
            if (e.character != '\0') return false;
            if (e.shift)
            {
                _panel.JumpToLive();
                _panel.Literal.Append(':');
            }
            else AppendSemicolon();
            e.Use();
            return true;
        }

        internal bool ForwardMappedKey(Event e)
        {
            var keyScreen = SessionHub.Instance.Screen(_panel.SessionName);
            string key = MapKey(e, keyScreen != null && keyScreen.AltScreen);
            if (key == null) return false;

            _panel.JumpToLive();
            _panel.Flush();
            SessionHub.Instance.Terminal.SendKeys(_panel.SessionName, new[] { key }, false);
            e.Use();
            return true;
        }

        // IMGUI drops semicolon's KeyDown before it reaches this window on this player. Unity still sends the character event.
        // DoWindowContents can run more than once per frame. The frame marker prevents duplicate input.
        internal void CaptureSemicolonInputCore()
        {
            if (_panel.AutoResumePending || _panel.SemicolonFrame == Time.frameCount ||
                !SessionHub.Instance.Online) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            string input = Input.inputString;
            int count = shift || string.IsNullOrEmpty(input) ? 0 : input.Count(c => c == ';');
            bool physical = !shift && Input.GetKeyDown(KeyCode.Semicolon);
            if (count == 0 && !physical) return;

            _panel.JumpToLive();
            _panel.Flush();
            SessionHub.Instance.Terminal.Paste(_panel.SessionName, new string(';', count > 0 ? count : 1));
            _panel.SemicolonFrame = Time.frameCount;
        }

        void AppendSemicolon()
        {
            if (_panel.SemicolonFrame == Time.frameCount) return;
            _panel.JumpToLive();
            _panel.Flush();
            SessionHub.Instance.Terminal.Paste(_panel.SessionName, ";");
            _panel.SemicolonFrame = Time.frameCount;
        }

        internal static bool IsSemicolonKey(KeyCode key) =>
            key == KeyCode.Semicolon || key == KeyCode.Colon;

        // Ignore slots past the end of the portrait list. Show stopped agents with their action controls.
        // Require an explicit action to start an agent.
        internal void SwitchToSlot(int slot)
        {
            var order = AgentColony.InBarOrder();
            if (slot >= order.Count) return;

            string name = order[slot];
            // Reopen the same agent's pane if a content view covers it.
            if (name == _panel.SessionName && _panel.Content == null) return;

            var info = SessionHub.Instance.Get(name);
            if (info == null) return;

            // Show the Agents tab when Alt+number selects an agent. This releases the content view being left.
            AgentSidebar.ShowWithoutHistory(SidebarTab.Agents);
            AgentSidebar.RememberAgent(name);

            _panel.SwitchTo(name);
        }

        // Move through the session list with Alt+Z or Alt+X. Chrome and pane handlers call this method.
        // Select sessions with no process too.
        internal static void WalkSession(int dir)
        {
            var order = TabOrder();
            if (order.Count == 0) return;

            string current = SessionSelectable.Current;
            int idx = -1;
            if (current != null)
                idx = order.IndexOf(current);

            int next = idx < 0
                ? (dir > 0 ? 0 : order.Count - 1)
                : (idx + dir + order.Count) % order.Count;

            string target = order[next];
            if (target == null) return;

            SessionSelectable.Current = target;
            Find.Selector?.ClearSelection();
            AgentSidebar.ShowWithoutHistory(SidebarTab.Agents);

            var info = SessionHub.Instance.Get(target);
            if (info == null) return;

            // Keep the current pane open when it already shows the target agent.
            var w = Find.WindowStack?.WindowOfType<TerminalWindow>();
            if (w != null && target == w.SessionName) return;

            TerminalWindow.Open(target);
        }

        // Start with visible sidebar rows in project order. Append hidden viewers, editors, folded agents, and temporary tabs.
        // Apply the project filter to appended sessions. Alt+number uses AgentColony.InBarOrder.
        internal static List<string> TabOrder()
        {
            var order = AgentSidebar.WalkOrder();
            var seen = new HashSet<string>(order);
            foreach (var info in SessionHub.Instance.Sessions)
            {
                if (info == null || !AgentSidebar.Passes(info.Project) ||
                    string.IsNullOrEmpty(info.Name) || !seen.Add(info.Name))
                    continue;
                order.Add(info.Name);
            }
            return order;
        }

        // The split menu lists sessions that can open from the sidebar. Keep it separate from TabOrder.
        // Tab reaches folded and temporary sessions. Exclude old diff processes without sidebar rows.
        internal static List<string> OpenBesideOrder()
        {
            var order = new List<string>();
            foreach (string session in TabOrder())
            {
                var info = SessionHub.Instance.Get(session);
                if (!AgentSidebar.IsOpenBesideCandidate(info)) continue;
                order.Add(session);
            }
            return order;
        }

    }
}
