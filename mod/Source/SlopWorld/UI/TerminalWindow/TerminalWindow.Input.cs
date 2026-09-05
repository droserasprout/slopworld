using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Owns terminal key and mouse policy. The Window facade supplies pane geometry and the
    // existing rendering/selection services, while this controller owns event ordering and
    // terminal input policy.
    sealed class TerminalInputController
    {
        readonly TerminalWindow _window;
        readonly Dictionary<KeyCode, System.Func<Event, bool>> _local;
        readonly Dictionary<KeyCode, System.Func<Event, bool>> _terminal;
        public TerminalInputController(TerminalWindow window)
        {
            _window = window;
            _local = new Dictionary<KeyCode, System.Func<Event, bool>>
            {
                { KeyCode.Escape, HandleEscapeKey },
                { KeyCode.Return, HandleReturnKey },
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
            // WindowStack may consume semicolon before the window body runs. Replay that one
            // character only; other Used events must not be replayed.
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
            if (e.type != EventType.KeyDown) return;

            // A pending Keyboard-page binding owns the next key, including keys normally
            // claimed by the sidebar or terminal chrome. Escape remains the chrome escape.
            if (ModOptions.KeyboardCaptureActive && e.keyCode != KeyCode.Escape) return;

            if (HandleFunctionKey(e)) { e.Use(); return; }

            if (e.keyCode == KeyCode.Escape)
            {
                _window.Leave();
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

        // Handle unshifted keys bound to the chrome. Read KeyBindingDefs so option-menu
        // rebindings apply; shifted/unbound keys pass to the agent. A chrome transition also
        // explicitly closes menus because it may replace focus before their body runs.
        public static bool HandleFunctionKey(Event e)
        {
            // Shift+key = pass through to the agent/tui.
            if (e.shift || e.keyCode == KeyCode.None) return false;

            if (Bound(ModDefOf.SlopCommandPalette, e))
            {
                UiMenu.CloseAll();
                SearchView.ReleaseFocus();
                CommandPalette.Toggle();
                return true;
            }
            if (Bound(ModDefOf.SlopSidebarAgents, e))
            {
                UiMenu.CloseAll();
                AgentSidebar.FocusTerminal();
                return true;
            }
            if (Bound(ModDefOf.SlopSidebarFiles, e))
            {
                UiMenu.CloseAll();
                AgentSidebar.ShowFiles();
                return true;
            }
            if (Bound(ModDefOf.SlopSidebarSearch, e))
            {
                UiMenu.CloseAll();
                AgentSidebar.ShowSearch();
                return true;
            }
            if (Bound(ModDefOf.SlopSidebarGit, e))
            {
                UiMenu.CloseAll();
                AgentSidebar.ShowGit();
                return true;
            }
            if (Bound(ModDefOf.SlopSidebarTasks, e))
            {
                UiMenu.CloseAll();
                AgentSidebar.ShowTasks();
                return true;
            }
            if (Bound(ModDefOf.SlopSidebarLibrary, e))
            {
                UiMenu.CloseAll();
                AgentSidebar.ShowLibrary();
                return true;
            }
            if (Bound(ModDefOf.SlopQuickTerminal, e))
            {
                // Use the same toggle as the map-layer component. In particular, a settings
                // view is content inside this window, so F12 must reveal/open its terminal
                // rather than close the host and stop there.
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

        // Whether this event's key is either of the def's two slots. Asked of the event
        // rather than through KeyBindingDef.KeyDownEvent, because the caller has already
        // taken the event and needs to know whether to Use it.
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
            // Window actions run before the terminal's online check. A bare Escape or an
            // ordinary Return returns false from the same handler and is dispatched below.
            if (TryLocal(e)) return;

            // Not in TerminalHotkeys: a window absorbing input makes
            // WindowStack.HandleEventsHighPriority Use every KeyDown, and that runs earlier in
            // UIRoot.UIRootOnGUI than any game component.
            // All F-keys go through one gate: bare = ours, Shift+F = agent.
            if (HandleFunctionKey(e)) { e.Use(); return; }

            // Ahead of the offline check: switching is local and the subscription survives a
            // dead socket, so a pane that will not change during a redeploy reads as hung.
            if (TryHandleSessionNavigation(e)) return;

            // Offline the hub drops sends, so count them for the banner rather than letting
            // the terminal silently eat what was typed.
            if (!SessionHub.Instance.Online)
            {
                if (e.keyCode != KeyCode.None || e.character != '\0')
                {
                    _window.DroppedKeys++;
                    e.Use();
                }
                return;
            }

            // Auto-resume is queued by the daemon after startup settles. Keep user input out
            // of the resume picker; chrome and navigation above remain available so the user
            // can leave this pane while it is being resumed.
            if (_window.AutoResumePending)
            {
                if (e.keyCode != KeyCode.None || e.character != '\0') e.Use();
                return;
            }

            // On this Unity player the literal semicolon arrives with a spurious modifier,
            // so the ordinary printable-input guard below rejects it. The character is the
            // layout-resolved answer; trust it instead of the broken modifier flags.
            if (e.character == ';')
            {
                AppendSemicolon();
                e.Use();
                return;
            }

            if (TryTerminal(e)) return;

            // Unity delivers printable input as a second event carrying only the character.
            if (e.character != '\0' && e.character != '\n' &&
                e.character != '\r' && e.character != '\t' && !e.control && !e.alt)
            {
                _window.JumpToLive();
                _window.Literal.Append(e.character);
                e.Use();
                return;
            }

            if (e.keyCode != KeyCode.None)
                e.Use(); // swallow it so RimWorld hotkeys don't fire behind us
        }

        // Session changes are local and remain available while the daemon is offline. Both the
        // chrome and pane handlers call this only after their own distinct gates have run.
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
            // Shift+Escape is the way out; a bare Escape must reach the agent.
            if (!e.shift) return false;
            _window.Close();
            e.Use();
            return true;
        }

        internal bool HandleReturnKey(Event e)
        {
            // Shift+Enter: send the kitty keyboard protocol sequence for Shift+Enter
            // (\e[13;2u) so apps like Claude Code can distinguish it from plain Enter
            // and insert a newline rather than submitting.
            if (!e.shift) return false;
            _window.JumpToLive();
            _window.Flush();
            SessionHub.Instance.SendKeys(_window.SessionName, new[] { "\u001b[13;2u" }, true);
            e.Use();
            return true;
        }

        internal bool HandleHistoryKey(Event e)
        {
            if (!e.shift || e.control || e.alt ||
                (e.keyCode != KeyCode.PageUp && e.keyCode != KeyCode.PageDown))
                return false;

            var live = SessionHub.Instance.Screen(_window.SessionName);
            // Alternate-screen applications own shifted page keys; the primary screen owns
            // them for terminal scrollback, just like a normal terminal emulator.
            if (_window.ScrollOffset == 0 && live != null && live.AltScreen) return false;

            int page = _window.Rows > 0 ? _window.Rows : live != null ? live.Rows : 1;
            page = Mathf.Max(1, page);
            bool up = e.keyCode == KeyCode.PageUp;
            bool fromLive = _window.ScrollOffset <= 0;
            if (up) _window.ScrollOffset += page;
            else _window.ScrollOffset = Mathf.Max(0, _window.ScrollOffset - page);
            _window.JumpHistoryTo(_window.ScrollOffset);
            _window.QueueScroll(up, fromLive);
            e.Use();
            return true;
        }

        internal bool HandleControlC(Event e)
        {
            // Not a Ctrl chord: still a key the mapper may forward (e.g. Alt+C -> M-c).
            if (!e.control) return ForwardMappedKey(e);
            // Terminal convention: Ctrl+Shift+C is always copy, and Ctrl+C copies
            // when text is selected (otherwise it passes through as SIGINT).
            if (_window.HasSelection)
            {
                _window.CopySelection();
                e.Use();
                return true;
            }
            // No selection: Ctrl+Shift+C is a no-op; bare Ctrl+C falls through to MapKey.
            if (e.shift)
            {
                e.Use();
                return true;
            }
            return ForwardMappedKey(e);
        }

        internal bool HandleControlV(Event e)
        {
            // Not a Ctrl chord: still a key the mapper may forward (e.g. Alt+V -> M-v).
            if (!e.control) return ForwardMappedKey(e);
            _window.JumpToLive();
            _window.PasteClipboard();
            e.Use();
            return true;
        }

        internal bool HandleSemicolonKey(Event e)
        {
            // Some backends omit the character-only event. Preserve the keyboard layout's
            // shifted form before the named key is swallowed below.
            if (e.character != '\0') return false;
            if (e.shift)
            {
                _window.JumpToLive();
                _window.Literal.Append(':');
            }
            else AppendSemicolon();
            e.Use();
            return true;
        }

        internal bool ForwardMappedKey(Event e)
        {
            var keyScreen = SessionHub.Instance.Screen(_window.SessionName);
            string key = MapKey(e, keyScreen != null && keyScreen.AltScreen);
            if (key == null) return false;

            _window.JumpToLive();
            _window.Flush();
            // Tips ride the Enter that is about to have breadcrumbs pasted in front of it, and
            // nothing else: `BreadcrumbsPending` is the daemon's answer to whether this is that
            // Enter.
            var info = SessionHub.Instance.Get(_window.SessionName);
            bool crumbs = key == "Enter" && info != null && info.BreadcrumbsPending;
            SessionHub.Instance.SendKeys(_window.SessionName, new[] { key }, false,
                crumbs ? Patch_LoadingTips.RandomTips(Patch_LoadingTips.TipBatch) : null);
            e.Use();
            return true;
        }

        // IMGUI loses semicolon's KeyDown before it reaches this window on this player, but
        // Unity's text-input stream still carries it (which is why ordinary game fields work).
        // DoWindowContents runs more than once per frame, and a surviving KeyDown may follow,
        // so the frame marker makes the two roads one keystroke.
        internal void CaptureSemicolonInputCore()
        {
            if (_window.AutoResumePending || _window.SemicolonFrame == Time.frameCount ||
                !SessionHub.Instance.Online) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            string input = Input.inputString;
            int count = shift || string.IsNullOrEmpty(input) ? 0 : input.Count(c => c == ';');
            bool physical = !shift && Input.GetKeyDown(KeyCode.Semicolon);
            if (count == 0 && !physical) return;

            _window.JumpToLive();
            _window.Flush();
            SessionHub.Instance.Paste(_window.SessionName, new string(';', count > 0 ? count : 1));
            _window.SemicolonFrame = Time.frameCount;
        }

        void AppendSemicolon()
        {
            if (_window.SemicolonFrame == Time.frameCount) return;
            _window.JumpToLive();
            _window.Flush();
            SessionHub.Instance.Paste(_window.SessionName, ";");
            _window.SemicolonFrame = Time.frameCount;
        }

        internal static bool IsSemicolonKey(KeyCode key) =>
            key == KeyCode.Semicolon || key == KeyCode.Colon;

        // A slot past the end is a no-op rather than a wrap: the keys are muscle memory for a
        // fixed portrait. In terminal mode a down agent is shown with its action gizmos;
        // starting it remains an explicit action.
        internal void SwitchToSlot(int slot)
        {
            var order = AgentColony.InBarOrder();
            if (slot >= order.Count) return;

            string name = order[slot];
            // The same agent while a view has the body is still a request to see it: the
            // number points the window at a portrait, and the pane is what a portrait is.
            if (name == _window.SessionName && _window.Content == null) return;

            var info = SessionHub.Instance.Get(name);
            if (info == null) return;

            // Alt+Num while the pane is open is about an agent: switch the sidebar to the
            // agents view, which releases whatever the view being left was showing.
            AgentSidebar.FocusTerminal();

            _window.SwitchTo(name);
        }

        // Walk the session list by dir (-1 or 1). Used from Alt+Z/Alt+X in both ChromeKeys
        // (content view up) and HandleKey (pane open). Sets the current session and switches
        // the pane, including when the target has no process.
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
            AgentSidebar.FocusTerminal();

            var info = SessionHub.Instance.Get(target);
            if (info == null) return;

            // The same agent while a pane is open is already on screen. A different agent
            // switches the pane.
            var w = Find.WindowStack?.WindowOfType<TerminalWindow>();
            if (w != null && target == w.SessionName) return;

            TerminalWindow.Open(target);
        }

        // The visible sidebar rows give the useful project-grouped order. Add sessions the
        // current view does not render afterward: routed viewers/editors, folded agents, and
        // host or other ephemeral tabs must still be reachable by tab cycling. Apply the
        // sidebar's project filter to those appended sessions too. Alt+Num stays on
        // AgentColony.InBarOrder and deliberately does not use this list.
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

        internal void HandleWheel(Rect body, Event e)
        {
            if (!body.Contains(e.mousePosition)) return;

            var live = SessionHub.Instance.Screen(_window.SessionName);
            bool editor = IsEditorSession();
            int step = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(e.delta.y)), 1, 5);
            bool up = e.delta.y < 0;

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
                _window.ClearSelection();
                var cell = _window.CellAt(body, e.mousePosition);
                string act = up ? "wheelup" : "wheeldown";
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
                _window.ClearSelection();
                var keys = new string[step];
                for (int k = 0; k < step; k++) keys[k] = up ? "Up" : "Down";
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

        static bool ShouldForwardMouse(ScreenBuf live, Event e) =>
            live != null && live.AppMouse && !e.shift;

        static bool IsPrimaryMouse(Event e) => e.button == 0;

        // A window can receive a mouse event after WindowStack has marked it Used. Keep the
        // original type for all terminal gesture dispatch; otherwise Ctrl+clicks (and ordinary
        // selection presses) disappear before the pane sees them.
        internal static EventType MouseType(Event e) =>
            e.type == EventType.Used ? e.rawType : e.type;

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
