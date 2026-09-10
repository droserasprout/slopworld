using UnityEngine;
using Verse;

namespace SlopWorld
{
    sealed partial class TerminalPanel
    {
        bool _opened;

        public override void Opened()
        {
            if (_opened) return;
            _opened = true;
            if (_name == null) return;
            _showStopped = SessionHub.Instance.Get(_name)?.Gone == true;
            SessionHub.Instance.Subscribe(_name);
            PrimePanelSize();
        }

        public override void Closed()
        {
            if (!_opened) return;
            _opened = false;
            ReleasePanelInput();
            ScrollDebugEnd();
            Drop();
            if (_name == null) return;
            SessionHub.Instance.Unsubscribe(_name);
            FilesView.CloseViewerIf(_name);
            SearchView.CloseViewerIf(_name);
            GitView.CloseViewerIf(_name);
        }

        internal void BindSession(string name)
        {
            if (name == _name) return;
            ReleasePanelInput();
            SaveScrollbackState(_name);
            _historyCoordinator.SaveCache(_name);
            // A window opened on content alone has no pane to let go of, and a subscription
            // named null is one the daemon would have to answer.
            if (_opened && _name != null) SessionHub.Instance.Unsubscribe(_name);
            _name = name;
            if (_name != null)
            {
                if (_opened) SessionHub.Instance.Subscribe(_name);
                TerminalRecall.Remember(_name);
            }
            _showStopped = _name != null && SessionHub.Instance.Get(_name)?.Gone == true;
            if (_opened) PrimePanelSize();
            _historyCoordinator.ResetForSession();
            RestoreScrollbackState(_name);
            _historyCoordinator.RestoreCache(_name);
            if (_scrollOff > 0)
            {
                _historyDisplayedFrame = CachedDisplayedFrame(_name);
                // The cached frame is already at the saved integer anchor. Seed the fallback
                // with its fractional translation so the first switched-tab repaint does not
                // briefly snap to the line boundary while history is reassembled.
                if (_historyDisplayedFrame != null && _historyJumpPixels >= 0f)
                {
                    float cellH = TerminalFont.CellH;
                    if (cellH > 0.01f)
                    {
                        float shift = _historyJumpPixels - _historyDisplayedFrame.Off * cellH;
                        if (Mathf.Abs(shift) < cellH)
                            _renderHistoryShift = shift;
                    }
                }
            }
            _selectionCoordinator.ResetForNewRun();
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
            var info = SessionHub.Instance.Get(name);
            _scrollbackStates[name] = new ScrollbackState(
                Mathf.Max(0, _scrollOff), pixels, info?.RunId ?? 0L);
        }

        void RestoreScrollbackState(string name)
        {
            if (string.IsNullOrEmpty(name) ||
                !_scrollbackStates.TryGetValue(name, out var state))
            {
                _scrollOff = 0;
                return;
            }

            var info = SessionHub.Instance.Get(name);
            if (info == null || state.RunId != info.RunId)
            {
                _scrollbackStates.Remove(name);
                _scrollOff = 0;
                return;
            }

            _scrollOff = Mathf.Clamp(state.Offset, 0, MaxScrollLines);
            _historyJumpPending = true;
            _historyJumpOff = _scrollOff;
            _historyJumpPixels = state.Pixels;
        }


        internal bool EnsureSession(SessionHub hub, bool covered)
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

                if (!covered)
                {
                    // Keep a durable agent's pane in place after its process exits. Do not
                    // update SessionSelectable.Current or open another session: the stopped
                    // pane is still the user's focus and its Start gizmo remains available.
                    if (info != null)
                    {
                        _showStopped = true;
                        return true;
                    }
                    return false;
                }
                hub.Unsubscribe(_name);
                _name = null;
            }
            else if (_name == null && !covered)
            {
                return false;
            }
            return true;
        }

    }
}
