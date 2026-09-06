using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // TerminalWindow session binding and Window lifecycle.
    public partial class TerminalWindow
    {
        public static TerminalWindow Open(string name)
        {
            // The current session follows the pane.
            SessionSelectable.Current = name;

            // Re-opening the same session should focus it, not stack a second copy. Asking
            // for a pane always puts the pane back, though, even the one already behind the
            // content: a portrait clicked while the options menu is up is a request to see
            // that agent.
            var existing = Find.WindowStack.WindowOfType<TerminalWindow>();
            if (existing != null)
            {
                if (existing._name == name) existing.Leave();
                else existing.SwitchTo(name);
                return existing;
            }

            var w = new TerminalWindow(name);
            Find.WindowStack.Add(w);
            TerminalRecall.Remember(name);
            return w;
        }

        // Opens whatever the chrome is being asked to show. With a pane already up the pane
        // stays behind it - Leave puts it back - and with nothing up the window opens on the
        // content alone, which is the options menu reached from the map.
        public static void OpenContent(IContentView view)
        {
            if (view == null || Find.WindowStack == null) return;

            var existing = Find.WindowStack.WindowOfType<TerminalWindow>();
            if (existing != null) { existing.SetContent(view); return; }

            var w = new TerminalWindow(null);
            w._content = view;
            Find.WindowStack.Add(w);
            view.Opened();
        }

        // The pane's session, and *only* while the pane is what is on show: with content up
        // there is no current agent, which is what keeps a row from reading as selected under
        // the options menu and what makes clicking that row open it again.
        public static string CurrentName
        {
            get
            {
                var w = Find.WindowStack?.WindowOfType<TerminalWindow>();
                return w == null || w._content != null ? null : w._name;
            }
        }

        // Content views hide the active session from CurrentName, but F12 still needs to know
        // whether leaving the view will reveal a pane or remove a content-only host.
        internal static bool HasBackingPane =>
            Find.WindowStack?.WindowOfType<TerminalWindow>()?._name != null;

        // A successful rename must not go through Open: that would reset the pane and can
        // briefly bind it to the old name while the sessions snapshot catches up. Keep the
        // existing window, scrollback and selection, changing only the session handle.
        internal static void RenameActive(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) ||
                oldName == newName) return;

            var window = Find.WindowStack?.WindowOfType<TerminalWindow>();
            bool active = window != null && window._name == oldName;
            if (active)
            {
                window._name = newName;
                TerminalRecall.Remember(newName);
            }

            if (!active && SessionSelectable.Current != oldName) return;
            SessionSelectable.Current = newName;
            if (active && window._content == null) SelectAgent(newName);
        }

        // What the chrome is showing, for anything that has to know which it is. Null is the
        // pane, and null window is neither.
        public static IContentView Showing =>
            Find.WindowStack?.WindowOfType<TerminalWindow>()?._content;

        // The one of a kind already up, so a door that opens a view can hand the same one
        // back rather than build a second: pressing `config` twice is a toggle, not a reset.
        public static T ShowingAs<T>() where T : class, IContentView => Showing as T;

        // Up means leave it; down means show it, and the view is built only in the second
        // case - the factory rather than an instance, so a press that turns out to be a
        // close asks the daemon for nothing. Every door onto a view takes this road, which
        // is what makes each of them a switch.
        public static void ToggleContent<T>(System.Func<T> make) where T : class, IContentView
        {
            if (ShowingAs<T>() != null)
            {
                Find.WindowStack?.WindowOfType<TerminalWindow>()?.Leave();
                return;
            }
            OpenContent(make());
        }

        void SetContent(IContentView view)
        {
            if (_content == view) return;
            _fieldLifetime.Cancel();
            _fieldLifetime = new FieldLifetime();
            _content?.Closed();
            _content = view;
            _content?.Opened();
        }

        // Out of the content and back to what is behind it: the pane it was opened over, or
        // the map when there was none. The chrome exists to show something.
        public void Leave()
        {
            if (_content == null) return;
            SetContent(null);
            if (_name == null) Close();
        }

        // The pane is on the Super layer, so an ordinary dialog opened from inside it would be
        // added underneath and never seen.
        public static void OpenOverPane(Window w)
        {
            if (Find.WindowStack == null) return;
            if (Find.WindowStack.WindowOfType<TerminalWindow>() != null)
                w.layer = WindowLayer.Super;
            Find.WindowStack.Add(w);
        }

        // Content views remain read-only, but a view opened over a pane can still offer the
        // terminal's paste action to the agent behind it. A view opened from the map has no
        // destination, so its Paste menu item is disabled.
        public static bool CanPasteClipboardToAgent =>
            Find.WindowStack?.WindowOfType<TerminalWindow>()?._name != null;

        public static void PasteClipboardToAgent()
        {
            var window = Find.WindowStack?.WindowOfType<TerminalWindow>();
            if (window == null || window._name == null) return;
            window.JumpToLive();
            window.PasteClipboard();
        }

        // PaneOverDraw reads this several times a frame, so the closed case costs one static
        // read. Open, it is checked against the stack: a flag left standing wrongly is a map
        // never drawn again.
        static bool _covering;

        public static bool Covering =>
            _covering && Find.WindowStack?.WindowOfType<TerminalWindow>() != null;

        // Rebinds the pane but keeps the window's place in the stack. Whatever was in the body
        // goes: being pointed at an agent is a request to see it. History rows belong to the
        // old session, while its scroll position is saved for the next visit.
        internal void SwitchTo(string name)
        {
            SetContent(null);
            if (name == _name) return;
            SaveScrollbackState(_name);
            // A window opened on content alone has no pane to let go of, and a subscription
            // named null is one the daemon would have to answer.
            if (_name != null) SessionHub.Instance.Unsubscribe(_name);
            _name = name;
            if (_name != null)
            {
                SessionHub.Instance.Subscribe(_name);
                TerminalRecall.Remember(_name);
                SelectAgent(_name);
            }
            _showStopped = _name != null && SessionHub.Instance.Get(_name)?.Gone == true;
            PrimeCachedSize();
            _scrollOff = 0;
            _wantedScrollOff = 0;
            _scrollPending = false;
            _nextScrollSend = 0f;
            _hasWheelDirection = false;
            _historyScrollReady = false;
            _historyJumpPending = false;
            _historyJumpPixels = -1f;
            _renderHistoryShift = 0f;
            _historyLastPixels = 0f;
            _historyTopOff = -1;
            _historyViewReady = false;
            _historyRefreshPending = false;
            _historyWarmed = false;
            _history.Reset();
            _historyRequests.Clear();
            _historyCoordinateShift = 0;
            _historyDisplayedFrame = null;
            RestoreScrollbackState(_name);
            if (_scrollOff > 0)
                _historyDisplayedFrame = CachedDisplayedFrame(_name);
            _selectionOff = 0;
            _lastLiveSeq = -1;
            ClearSelection();
            ResetCursorBlink();
        }

        void SaveScrollbackState(string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            // `_historyScroll` may still hold the old position for one IMGUI pass after an
            // input jumps to live output. The integer mode is authoritative in that case.
            float pixels = 0f;
            if (_scrollOff > 0)
            {
                if (_historyScrollReady) pixels = HistoryOffsetPixels();
                else if (_historyJumpPixels >= 0f) pixels = _historyJumpPixels;
                else
                {
                    float cellH = TerminalFont.CellH;
                    pixels = cellH > 0.01f ? _scrollOff * cellH : -1f;
                }
            }
            _scrollbackStates[name] = new ScrollbackState(Mathf.Max(0, _scrollOff), pixels);
        }

        void RestoreScrollbackState(string name)
        {
            if (string.IsNullOrEmpty(name) ||
                !_scrollbackStates.TryGetValue(name, out var state))
            {
                _scrollOff = 0;
                return;
            }

            _scrollOff = Mathf.Clamp(state.Offset, 0, MaxScrollLines);
            _historyJumpPending = true;
            _historyJumpOff = _scrollOff;
            _historyJumpPixels = state.Pixels;
        }


        // Clearing first: the brackets' jump-out is an animation off SelectionDrawer's select
        // time, so a pawn already selected would never replay it.
        static void SelectAgent(string session)
        {
            var pawn = AgentColony.Current?.PawnOf(session);
            if (pawn == null) return;
            Find.Selector.ClearSelection();
            Find.Selector.Select(pawn);
        }

        public override void PreOpen()
        {
            base.PreOpen();
            _covering = true;
            if (_name == null) return;
            _showStopped = SessionHub.Instance.Get(_name)?.Gone == true;
            SessionHub.Instance.Subscribe(_name);
            SelectAgent(_name);
            PrimeCachedSize();
        }

        public override void PostClose()
        {
            _fieldLifetime.Cancel();
            base.PostClose();
            ScrollDebugEnd();
            _covering = false;
            Drop(); // a screen's worth of VRAM, held for a window that is gone
            // The window is what the view was being shown in, so it is closed with it.
            SetContent(null);
            if (_name == null) return;
            SessionHub.Instance.Unsubscribe(_name);
            // The terminal is a pager's only home: closing it while a file was being read, or
            // a diff, means the focus has moved away. Both are asked - the window does not
            // know which view opened what, and only one of them can be showing this session.
            FilesView.CloseViewerIf(_name);
            SearchView.CloseViewerIf(_name);
            GitView.CloseViewerIf(_name);
        }

        bool EnsureSession(SessionHub hub)
        {
            var info = hub.Get(_name);
            if (info != null && info.Alive) _showStopped = false;
            // An agent that exits during normal terminal use stays in its pane. An
            // intentionally selected stopped agent is held for its action gizmos; content
            // views keep the window for their chrome.
            if (_name != null && (info == null || info.Gone))
            {
                // A rename event removes the old name before the save response retargets this
                // window. Hold the pane through that expected gap; otherwise the normal exit
                // handoff steals focus from the agent being renamed.
                if (hub.TryPendingRename(_name, out _)) return true;

                ResetHistoryForNewRun();

                if (_content == null)
                {
                    // Keep a durable agent's pane in place after its process exits. Do not
                    // update SessionSelectable.Current or open another session: the stopped
                    // pane is still the user's focus and its Start gizmo remains available.
                    if (info != null)
                    {
                        _showStopped = true;
                        return true;
                    }
                    Close();
                    return false;
                }
                hub.Unsubscribe(_name);
                _name = null;
            }
            else if (_name == null && _content == null)
            {
                Close();
                return false;
            }
            return true;
        }

        public static Color StateColor(AgentState s)
        {
            switch (s)
            {
                case AgentState.Working: return UiWidgets.StateWorking;
                case AgentState.Waiting: return UiWidgets.StateWaiting;
                case AgentState.Idle: return UiWidgets.StateIdle;
                default: return UiWidgets.StateDown;
            }
        }
    }
}
