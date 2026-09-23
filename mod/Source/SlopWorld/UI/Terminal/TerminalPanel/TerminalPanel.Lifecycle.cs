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
            if (_state.Name == null) return;
            _state.ShowStopped = SessionHub.Instance.Get(_state.Name)?.Gone == true;
            SessionHub.Instance.Subscribe(_state.Name);
            PrimePanelSize();
        }

        public override void Closed()
        {
            if (!_opened) return;
            _opened = false;
            ReleasePanelInput();
            ScrollDebugEnd();
            Drop();
            if (_state.Name == null) return;
            SessionHub.Instance.Unsubscribe(_state.Name);
            AgentSidebar.TerminalClosed(_state.Name);
        }

        internal void BindSession(string name)
        {
            if (name == _state.Name) return;
            ReleasePanelInput();
            SaveScrollbackState(_state.Name);
            _historyCoordinator.SaveCache(_state.Name);
            // A content-only window has no terminal pane to unsubscribe.
            // Do not send an unsubscribe request for a null session.
            if (_opened && _state.Name != null) SessionHub.Instance.Unsubscribe(_state.Name);
            _state.Name = name;
            if (_state.Name != null)
            {
                if (_opened) SessionHub.Instance.Subscribe(_state.Name);
                TerminalRecall.Remember(_state.Name);
            }
            _state.ShowStopped = _state.Name != null &&
                SessionHub.Instance.Get(_state.Name)?.Gone == true;
            if (_opened) PrimePanelSize();
            _historyCoordinator.ResetForSession();
            RestoreScrollbackState(_state.Name);
            _historyCoordinator.RestoreCache(_state.Name);
            if (_scrollOff > 0)
            {
                _historyDisplayedFrame = CachedDisplayedFrame(_state.Name);
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
            var info = hub.Get(_state.Name);
            if (info != null && info.Alive) _state.ShowStopped = false;
            // Keep the pane open when an agent exits during normal terminal use.
            // Keep a selected stopped agent visible so its action gizmos remain available.
            // Keep content views open so the window can show their chrome.
            if (_state.Name != null && (info == null || info.Gone))
            {
                // A rename event removes the old name before the save response retargets this
                // window. Hold the pane through that expected gap. Otherwise the normal exit
                // handoff steals focus from the agent being renamed.
                if (hub.TryPendingRename(_state.Name, out _)) return true;

                ResetHistoryForNewRun();

                if (!covered)
                {
                    // Keep a durable agent's pane in place after its process exits. Do not
                    // update SessionSelectable.Current or open another session: the stopped
                    // pane is still the user's focus and its Start gizmo remains available.
                    if (info != null)
                    {
                        _state.ShowStopped = true;
                        return true;
                    }
                    return false;
                }
                hub.Unsubscribe(_state.Name);
                _state.Name = null;
            }
            else if (_state.Name == null && !covered)
            {
                return false;
            }
            return true;
        }

    }
}
