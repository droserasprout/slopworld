using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Mechanical split: TerminalWindow.Input methods.
    public partial class TerminalWindow
    {
        // Handle unshifted keys bound to the chrome. Read KeyBindingDefs so option-menu
        // rebindings apply; shifted/unbound keys pass to the agent. A chrome transition also
        // explicitly closes menus because it may replace focus before their body runs.
        public static bool HandleFunctionKey(Event e)
        {
            // Shift+key = pass through to the agent/tui.
            if (e.shift || e.keyCode == KeyCode.None) return false;

            if (Bound(SlopDefOf.SlopCommandPalette, e))
            {
                SlopMenu.CloseAll();
                SearchView.ReleaseFocus();
                CommandPalette.Toggle();
                return true;
            }
            if (Bound(SlopDefOf.SlopSidebarAgents, e))
            {
                SlopMenu.CloseAll();
                AgentSidebar.FocusTerminal();
                return true;
            }
            if (Bound(SlopDefOf.SlopSidebarFiles, e))
            {
                SlopMenu.CloseAll();
                AgentSidebar.ShowFiles();
                return true;
            }
            if (Bound(SlopDefOf.SlopSidebarSearch, e))
            {
                SlopMenu.CloseAll();
                AgentSidebar.ShowSearch();
                return true;
            }
            if (Bound(SlopDefOf.SlopSidebarGit, e))
            {
                SlopMenu.CloseAll();
                AgentSidebar.ShowGit();
                return true;
            }
            if (Bound(SlopDefOf.SlopSidebarTasks, e))
            {
                SlopMenu.CloseAll();
                AgentSidebar.ShowTasks();
                return true;
            }
            if (Bound(SlopDefOf.SlopSidebarLibrary, e))
            {
                SlopMenu.CloseAll();
                AgentSidebar.ShowLibrary();
                return true;
            }
            if (Bound(SlopDefOf.SlopQuickTerminal, e))
            {
                // Close if the window is open, open one if not (handles both map and
                // pane contexts via the same check).
                SlopMenu.CloseAll();
                var w = Find.WindowStack?.WindowOfType<TerminalWindow>();
                if (w != null) w.Close();
                else AgentSidebar.FocusTerminal();
                return true;
            }
            if (Bound(SlopDefOf.SlopToggleFullscreen, e))
            {
                SlopMenu.CloseAll();
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
            if (_input.TryLocal(e)) return;

            // Not in TerminalHotkeys: a window absorbing input makes
            // WindowStack.HandleEventsHighPriority Use every KeyDown, and that runs earlier in
            // UIRoot.UIRootOnGUI than any game component.
            // All F-keys go through one gate: bare = ours, Shift+F = agent.
            if (HandleFunctionKey(e)) { e.Use(); return; }

            // Ahead of the offline check: switching is local and the subscription survives a
            // dead socket, so a pane that will not change during a redeploy reads as hung.
            int slot = TerminalHotkeys.SlotKey(e);
            if (slot >= 0 && e.alt)
            {
                SwitchToSlot(slot);
                e.Use();
                return;
            }

            if (TryTabWalkDirection(e, out var dir))
            {
                WalkSession(dir);
                e.Use();
                return;
            }

            // Alt+comma/Alt+period: walk the session list while a pane is open. Bare
            // comma/dot belong to the agent; the alt prefix is the chrome's own walk.
            if (e.alt && (e.keyCode == KeyCode.Comma || e.keyCode == KeyCode.Period))
            {
                WalkSession(e.keyCode == KeyCode.Period ? 1 : -1);
                e.Use();
                return;
            }

            // Offline the hub drops sends, so count them for the banner rather than letting
            // the terminal silently eat what was typed.
            if (!SessionHub.Instance.Online)
            {
                if (e.keyCode != KeyCode.None || e.character != '\0')
                {
                    _droppedKeys++;
                    e.Use();
                }
                return;
            }

            // Auto-resume is queued by the daemon after startup settles. Keep user input out
            // of the resume picker; chrome and navigation above remain available so the user
            // can leave this pane while it is being resumed.
            if (AutoResumePending)
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

            if (_input.TryTerminal(e)) return;

            // Unity delivers printable input as a second event carrying only the character.
            if (e.character != '\0' && e.character != '\n' &&
                e.character != '\r' && e.character != '\t' && !e.control && !e.alt)
            {
                JumpToLive();
                _literal.Append(e.character);
                e.Use();
                return;
            }

            if (e.keyCode != KeyCode.None)
                e.Use(); // swallow it so RimWorld hotkeys don't fire behind us
        }

        internal bool HandleEscapeKey(Event e)
        {
            // Shift+Escape is the way out; a bare Escape must reach the agent.
            if (!e.shift) return false;
            Close();
            e.Use();
            return true;
        }

        internal bool HandleReturnKey(Event e)
        {
            // Shift+Enter: send the kitty keyboard protocol sequence for Shift+Enter
            // (\e[13;2u) so apps like Claude Code can distinguish it from plain Enter
            // and insert a newline rather than submitting.
            if (!e.shift) return false;
            JumpToLive();
            Flush();
            SessionHub.Instance.SendKeys(_name, new[] { "\u001b[13;2u" }, true);
            e.Use();
            return true;
        }

        internal bool HandleHistoryKey(Event e)
        {
            if (!e.shift || e.control || e.alt ||
                (e.keyCode != KeyCode.PageUp && e.keyCode != KeyCode.PageDown))
                return false;

            var live = SessionHub.Instance.Screen(_name);
            // Alternate-screen applications own shifted page keys; the primary screen owns
            // them for terminal scrollback, just like a normal terminal emulator.
            if (_scrollOff == 0 && live != null && live.AltScreen) return false;

            int page = _rows > 0 ? _rows : live != null ? live.Rows : 1;
            page = Mathf.Max(1, page);
            bool up = e.keyCode == KeyCode.PageUp;
            bool fromLive = _scrollOff <= 0;
            if (up) _scrollOff += page;
            else _scrollOff = Mathf.Max(0, _scrollOff - page);
            JumpHistoryTo(_scrollOff);
            QueueScroll(up, fromLive);
            e.Use();
            return true;
        }

        internal bool HandleControlC(Event e)
        {
            // Not a Ctrl chord: still a key the mapper may forward (e.g. Alt+C -> M-c).
            if (!e.control) return ForwardMappedKey(e);
            // Terminal convention: Ctrl+Shift+C is always copy, and Ctrl+C copies
            // when text is selected (otherwise it passes through as SIGINT).
            if (_hasSel)
            {
                CopySelection();
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
            JumpToLive();
            PasteClipboard();
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
                JumpToLive();
                _literal.Append(':');
            }
            else AppendSemicolon();
            e.Use();
            return true;
        }

        internal bool ForwardMappedKey(Event e)
        {
            var keyScreen = SessionHub.Instance.Screen(_name);
            string key = MapKey(e, keyScreen != null && keyScreen.AltScreen);
            if (key == null) return false;

            JumpToLive();
            Flush();
            // Tips ride the Enter that is about to have breadcrumbs pasted in front of it, and
            // nothing else: `BreadcrumbsPending` is the daemon's answer to whether this is that
            // Enter.
            var info = SessionHub.Instance.Get(_name);
            bool crumbs = key == "Enter" && info != null && info.BreadcrumbsPending;
            SessionHub.Instance.SendKeys(_name, new[] { key }, false,
                crumbs ? Patch_LoadingTips.RandomTips(Patch_LoadingTips.TipBatch) : null);
            e.Use();
            return true;
        }

        // IMGUI loses semicolon's KeyDown before it reaches this window on this player, but
        // Unity's text-input stream still carries it (which is why ordinary game fields work).
        // DoWindowContents runs more than once per frame, and a surviving KeyDown may follow,
        // so the frame marker makes the two roads one keystroke.
        internal void CaptureSemicolonInput()
        {
            if (AutoResumePending || _semicolonFrame == Time.frameCount ||
                !SessionHub.Instance.Online) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            string input = Input.inputString;
            int count = shift || string.IsNullOrEmpty(input) ? 0 : input.Count(c => c == ';');
            bool physical = !shift && Input.GetKeyDown(KeyCode.Semicolon);
            if (count == 0 && !physical) return;

            JumpToLive();
            Flush();
            SessionHub.Instance.Paste(_name, new string(';', count > 0 ? count : 1));
            _semicolonFrame = Time.frameCount;
        }

        void AppendSemicolon()
        {
            if (_semicolonFrame == Time.frameCount) return;
            JumpToLive();
            Flush();
            SessionHub.Instance.Paste(_name, ";");
            _semicolonFrame = Time.frameCount;
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
            if (name == _name && _content == null) return;

            var info = SessionHub.Instance.Get(name);
            if (info == null) return;

            // Alt+Num while the pane is open is about an agent: switch the sidebar to the
            // agents view, which releases whatever the view being left was showing.
            AgentSidebar.FocusTerminal();

            SwitchTo(name);
        }

        // Walk the session list by dir (-1 or 1). Used from Alt+Z/Alt+X and
        // Alt+comma/Alt+period in both ChromeKeys (content view up) and HandleKey (pane
        // open). Sets the current session and switches the pane, including when the target
        // has no process.
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
            if (w != null && target == w._name) return;

            Open(target);
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

            var live = SessionHub.Instance.Screen(_name);
            bool editor = IsEditorSession();
            int step = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(e.delta.y)), 1, 5);
            bool up = e.delta.y < 0;

            // Already in scrollback: stay there, whatever the live app is doing.
            // The app mode check below would otherwise hijack the wheel and send it
            // into the live app while the user is reading historical output.
            if (_scrollOff > 0)
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
                ClearSelection();
                var cell = CellAt(body, e.mousePosition);
                string act = up ? "wheelup" : "wheeldown";
                SessionHub.Instance.SendMouse(_name, act, 0, cell.x, cell.y, step);
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
                ClearSelection();
                var keys = new string[step];
                for (int k = 0; k < step; k++) keys[k] = up ? "Up" : "Down";
                SessionHub.Instance.SendKeys(_name, keys, false);
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
            var info = SessionHub.Instance.Get(_name);
            if (info == null) return false;
            if (Pager.IsEditorCommand(info.Cmd)) return true;
            return info.Ephemeral &&
                (info.Name ?? "").StartsWith("edit-", System.StringComparison.Ordinal);
        }

        // Send the first event immediately, then throttle repeats; direction changes bypass
        // the delay so reversals respond in one round trip. `fromLive` matters after a pane
        // has been in app-owned scrolling or has switched sessions: neither path necessarily
        // reset the old wheel direction, but the first event entering our history is still a
        // new gesture.
        bool _lastWheelUp;
        bool _hasWheelDirection;

        void QueueScroll(bool up, bool fromLive = false, int requestOff = -1)
        {
            float now = Time.realtimeSinceStartup;
            _wantedScrollOff = requestOff >= 0 ? requestOff : _scrollOff;
            _scrollPending = true;
            bool fresh = fromLive || !_hasWheelDirection || up != _lastWheelUp ||
                now >= _nextScrollSend;
            _lastWheelUp = up;
            _hasWheelDirection = true;
            if (fresh)
                SendPendingScroll();
        }

        internal void HandleMouse(Rect body, Event e)
        {
            // The pane's own menu is reachable in every mode, including a full-screen TUI.
            if (IsContextMenuEvent(e))
            {
                if (IsMouseDownInside(body, e))
                {
                    string url = LinkUnder(body, e.mousePosition);
                    int line = 0;
                    string menuPath = ControlHeld(e)
                        ? PathUnder(body, e.mousePosition, out line) : null;
                    if (url != null || !IsRelativePath(menuPath))
                    {
                        menuPath = null;
                        line = 0;
                    }
                    OpenMenu(url, menuPath, line);
                }
                e.Use();
                return;
            }

            // Terminal middle-click is the conventional paste gesture. Handle it before app
            // mouse reporting: the primary selection belongs to the terminal even while an
            // alternate-screen application has enabled mouse mode.
            if (IsPrimaryPasteEvent(body, e))
            {
                JumpToLive();
                PastePrimarySelection();
                e.Use();
                return;
            }

            // A URL printed inside a TUI is over something that wants the mouse as often as
            // not, so Ctrl+click takes precedence over app mouse reporting.
            if (IsLinkClick(body, e))
            {
                OpenUrl(LinkUnder(body, e.mousePosition));
                e.Use();
                return;
            }

            if (IsPathClick(body, e, out string path))
            {
                var session = SessionHub.Instance.Get(_name);
                if (session != null && FilesView.FocusPath(session.Project, path))
                {
                    e.Use();
                    return;
                }
            }

            // Multi-click selection is the terminal's gesture even when the app reports clicks.
            if (IsPrimaryClick(body, e))
            {
                int clickCount = _clicks.Observe(e, Time.realtimeSinceStartup);
                if (clickCount >= 2)
                {
                    CaptureSelection(body);
                    SelectClickedWord(body, e, clickCount);
                    if (clickCount >= 3) _clicks.Reset();
                    e.Use();
                    return;
                }
            }

            var live = SessionHub.Instance.Screen(_name);
            // Shift forces our own selection, like a real terminal.
            if (ShouldForwardMouse(live, e) && HandleMouseForward(body, e)) return;
            if (!IsPrimaryMouse(e)) return;

            HandleSelectionMouse(body, e);
        }

        static bool IsContextMenuEvent(Event e) => e.button == 1;

        static bool IsPrimaryPasteEvent(Rect body, Event e) =>
            MouseType(e) == EventType.MouseDown && e.button == 2 && body.Contains(e.mousePosition);

        static bool IsMouseDownInside(Rect body, Event e) =>
            MouseType(e) == EventType.MouseDown && body.Contains(e.mousePosition);

        bool IsLinkClick(Rect body, Event e) =>
            MouseType(e) == EventType.MouseDown && e.button == 0 && ControlHeld(e) &&
            body.Contains(e.mousePosition) && LinkUnder(body, e.mousePosition) != null;

        bool IsPathClick(Rect body, Event e, out string path)
        {
            path = null;
            if (MouseType(e) != EventType.MouseDown || e.button != 0 || !ControlHeld(e) ||
                !body.Contains(e.mousePosition)) return false;
            int line;
            path = PathUnder(body, e.mousePosition, out line);
            return path != null;
        }

        static bool IsRelativePath(string path) => !string.IsNullOrEmpty(path) && path[0] != '/';

        static bool IsPrimaryClick(Rect body, Event e) =>
            MouseType(e) == EventType.MouseDown && e.button == 0 &&
            body.Contains(e.mousePosition);

        static bool ShouldForwardMouse(ScreenBuf live, Event e) =>
            live != null && live.AppMouse && !e.shift;

        static bool IsPrimaryMouse(Event e) => e.button == 0;

        // A window can receive a mouse event after WindowStack has marked it Used. Keep the
        // original type for all terminal gesture dispatch; otherwise Ctrl+clicks (and ordinary
        // selection presses) disappear before the pane sees them.
        static EventType MouseType(Event e) =>
            e.type == EventType.Used ? e.rawType : e.type;

        static bool ControlHeld(Event e) =>
            e.control || e.command || Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.RightControl);

        void SelectClickedWord(Rect body, Event e, int clickCount)
        {
            var cell = CellAt(body, e.mousePosition);
            if (clickCount >= 3) TripleClickSelect(cell.y);
            else DoubleClickSelect(cell);
        }

        void HandleSelectionMouse(Rect body, Event e)
        {
            switch (MouseType(e))
            {
                case EventType.MouseDown:
                    BeginSelection(body, e);
                    return;
                case EventType.MouseDrag:
                    ContinueSelection(body, e);
                    return;
                case EventType.MouseUp:
                    FinishSelection(body, e);
                    return;
            }
        }

        void BeginSelection(Rect body, Event e)
        {
            if (!body.Contains(e.mousePosition)) return;
            _selA = _selB = CellAt(body, e.mousePosition);
            _selectionMouse = e.mousePosition;
            _selectionEdgeDirection = 0;
            _selectionEdgeFrame = -1;
            _dragging = true;
            _selectionMoved = false;
            _multiClickSelection = false;
            _wordDragging = false;
            _lineDragging = false;
            _hasSel = false;
            CaptureSelection(body);
            e.Use();
        }

        void ContinueSelection(Rect body, Event e)
        {
            if (!_dragging) return;
            _selectionMouse = e.mousePosition;
            _selectionMoved = true;
            var cell = CellAt(body, e.mousePosition);
            if (e.mousePosition.y < body.y || e.mousePosition.y >= body.yMax)
            {
                int rows = Mathf.Max(1, _rows > 0 ? _rows :
                    SessionHub.Instance.Screen(_name)?.Rows ?? 1);
                cell.y = Mathf.Clamp(cell.y, 0, rows - 1);
            }
            if (_lineDragging) SelectLineRange(_lineStart, cell.y);
            else if (_wordDragging) UpdateWordSelection(cell);
            else
            {
                _selB = cell;
                _hasSel = true;
            }
            e.Use();
        }

        void FinishSelection(Rect body, Event e)
        {
            if (!_dragging) return;
            var cell = CellAt(body, e.mousePosition);
            if (e.mousePosition.y < body.y || e.mousePosition.y >= body.yMax)
            {
                int rows = Mathf.Max(1, _rows > 0 ? _rows :
                    SessionHub.Instance.Screen(_name)?.Rows ?? 1);
                cell.y = Mathf.Clamp(cell.y, 0, rows - 1);
            }
            // A double click selects a word and a triple click replaces it with a row. Do not
            // copy the intermediate word to CLIPBOARD; the completed triple-click line is
            // published to PRIMARY by TripleClickSelect, while Ctrl+C and the Copy menu use
            // CLIPBOARD.
            bool copy = !_multiClickSelection || _selectionMoved;
            if (_lineDragging)
            {
                SelectLineRange(_lineStart, cell.y);
                _lineDragging = false;
                _dragging = false;
                _selectionMoved = false;
                ReleaseSelection();
                if (copy) CopySelection();
            }
            else if (_wordDragging)
            {
                UpdateWordSelection(cell);
                _wordDragging = false;
                _dragging = false;
                ReleaseSelection();
                if (_hasSel && copy) CopySelection();
            }
            else
            {
                _dragging = false;
                _selB = cell;
                if (_selectionMoved || _selA != _selB)
                {
                    _hasSel = true;
                    if (copy) CopySelection();
                }
                else _hasSel = false;
                _selectionMoved = false;
                ReleaseSelection();
            }
            _selectionMoved = false;
            _multiClickSelection = false;
            _selectionEdgeDirection = 0;
            _selectionEdgeFrame = -1;
            e.Use();
        }

        void JumpToLive()
        {
            _scrollOff = 0;
            _wantedScrollOff = 0;
            _scrollPending = false;
            _nextScrollSend = 0f;
            _hasWheelDirection = false;
            JumpHistoryTo(0);
            ResetCursorBlink();
        }

        void ResetCursorBlink() => _cursorBlinkAt = Time.realtimeSinceStartup;

        void Flush()
        {
            if (_literal.Length == 0) return;
            SessionHub.Instance.SendKeys(_name, new[] { _literal.ToString() }, true);
            _literal.Length = 0;
        }

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
