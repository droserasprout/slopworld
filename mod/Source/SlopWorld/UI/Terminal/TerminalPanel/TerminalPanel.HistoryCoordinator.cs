using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    sealed partial class TerminalPanel
    {
        // History owns daemon-offset translation and stale-reply decisions. Scrolling keeps the
        // local fractional gesture; this owner decides which immutable frame represents it.
        sealed class TerminalHistoryCoordinator
        {
            readonly TerminalPanel _panel;

            public TerminalHistoryCoordinator(TerminalPanel window)
            {
                _panel = window;
            }

            public ScreenBuf DisplayedScreen()
            {
                var hub = SessionHub.Instance;
                var live = hub.Screen(_panel._state.Name);
                _panel.NoteLiveFrame(live);
                DrainReplies(hub, live);
                if (_panel._scrollOff <= 0)
                {
                    _panel._historyViewReady = false;
                    _panel.SyncSelectionOffset(0);
                    return live;
                }

                float cellH = TerminalFont.CellH;
                float lines = cellH > 0.01f
                    ? _panel.HistoryOffsetPixels() / cellH : _panel._scrollOff;
                int anchor = Mathf.Max(0, Mathf.CeilToInt(lines - 0.0001f));
                bool extra = Mathf.Abs(lines - Mathf.Round(lines)) > 0.0001f;
                float viewStarted = _panel.ScrollDebugTimer();
                _panel._historyViewReady = _panel._history.TryView(
                    anchor, extra, out var displayed);
                _panel.ScrollDebugTryView(viewStarted);
                _panel.ScrollDebugDisplayable(anchor, displayed, _panel._historyViewReady);
                displayed = displayed ?? _panel._historyDisplayedFrame ?? live;
                if (displayed != null) _panel._historyDisplayedFrame = displayed;
                if (displayed != null) _panel.SyncSelectionOffset(displayed.Off);
                return displayed;
            }

            public void SaveCache(string name)
            {
                if (string.IsNullOrEmpty(name)) return;
                PruneCaches();
                var info = SessionHub.Instance.Get(name);
                if (info == null) return;

                _panel._historyCaches[name] = new HistoryCacheState
                {
                    History = _panel._history,
                    TopOffset = _panel._historyTopOff,
                    CoordinateShift = _panel._historyCoordinateShift,
                    Warmed = _panel._historyWarmed,
                    RefreshPending = _panel._historyRefreshPending,
                    RunId = info.RunId,
                    ConnectionGeneration = SessionHub.Instance.ConnectionGeneration,
                    LiveSeq = _panel._historyLiveSeq,
                    LiveHistory = _panel._historyLiveHistory,
                    Cols = _panel._historyLiveCols > 0
                        ? _panel._historyLiveCols : _panel._history.Cols,
                    Rows = _panel._historyLiveRows > 0
                        ? _panel._historyLiveRows : _panel._history.Rows,
                    AltScreen = _panel._historyLiveSeq >= 0
                        ? _panel._historyLiveAltScreen : _panel._history.AltScreen,
                };
            }

            public void RestoreCache(string name)
            {
                _panel._activeHistoryCache = null;
                _panel._historyRestorePending = false;
                if (string.IsNullOrEmpty(name) ||
                    !_panel._historyCaches.TryGetValue(name, out var cache))
                    return;

                var info = SessionHub.Instance.Get(name);
                if (info == null || cache.RunId != info.RunId ||
                    cache.ConnectionGeneration != SessionHub.Instance.ConnectionGeneration)
                {
                    _panel._historyCaches.Remove(name);
                    return;
                }

                _panel._activeHistoryCache = cache;
                _panel._history = cache.History ?? new TerminalHistory();
                _panel._historyTopOff = cache.TopOffset;
                _panel._historyCoordinateShift = cache.CoordinateShift;
                _panel._historyWarmed = cache.Warmed;
                _panel._historyRefreshPending = cache.RefreshPending;
                _panel._historyLiveSeq = cache.LiveSeq;
                _panel._historyLiveHistory = cache.LiveHistory;
                _panel._historyLiveCols = cache.Cols;
                _panel._historyLiveRows = cache.Rows;
                _panel._historyLiveAltScreen = cache.AltScreen;
                _panel._historyRestorePending = true;
            }

            public int RestoredShift(ScreenBuf live)
            {
                if (!_panel._historyRestorePending || live == null) return int.MinValue;
                _panel._historyRestorePending = false;

                var info = SessionHub.Instance.Get(_panel._state.Name);
                bool compatible = info != null && _panel._activeHistoryCache != null &&
                    _panel._activeHistoryCache.RunId == info.RunId &&
                    _panel._activeHistoryCache.ConnectionGeneration ==
                        SessionHub.Instance.ConnectionGeneration &&
                    (_panel._historyLiveCols <= 0 || live.Cols <= 0 ||
                        _panel._historyLiveCols == live.Cols) &&
                    (_panel._historyLiveRows <= 0 || live.Rows <= 0 ||
                        _panel._historyLiveRows == live.Rows) &&
                    _panel._historyLiveAltScreen == live.AltScreen;

                int shift = 0;
                if (compatible && _panel._historyLiveSeq >= 0 &&
                    live.Seq == _panel._historyLiveSeq)
                {
                    compatible = _panel._historyLiveHistory < 0 || live.History < 0 ||
                        live.History == _panel._historyLiveHistory;
                }
                else if (compatible && _panel._historyLiveHistory >= 0 && live.History >= 0)
                {
                    shift = live.History - _panel._historyLiveHistory;
                    compatible = shift >= 0;
                }
                else if (compatible && _panel._historyLiveSeq >= 0 &&
                         live.Seq != _panel._historyLiveSeq)
                {
                    compatible = false;
                }

                if (compatible) return shift;

                _panel._activeHistoryCache = null;
                _panel._history = new TerminalHistory();
                _panel._historyTopOff = -1;
                _panel._historyCoordinateShift = 0;
                _panel._historyWarmed = false;
                _panel._historyRefreshPending = false;
                _panel._historyDisplayedFrame = null;
                return 0;
            }

            public bool InputEnabled(ScreenBuf live) =>
                _panel._scrollOff > 0 || live == null ||
                (!live.AppMouse && !live.AltScreen && !_panel.IsEditorSession());

            public void ResetForNewRun()
            {
                if (_panel._selectionCoordinator.LastLiveSeq < 0 &&
                    !_panel._historyWarmed && _panel._historyRequests.Count == 0 &&
                    _panel._scrollOff == 0)
                    return;

                _panel._selectionCoordinator.ResetLiveSequence();
                _panel._historyCaches.Remove(_panel._state.Name);
                _panel._scrollbackStates.Remove(_panel._state.Name);
                ResetCommon();
                _panel._sentScrollOff = 0;
                _panel._historyJumpPending = true;
                _panel._historyJumpOff = 0;
                _panel._historyJumpPixels = -1f;
                _panel._selectionCoordinator.ResetForNewRun();
                _panel.ClearSelection();
            }

            public void ResetForSession()
            {
                ResetCommon();
                _panel._hasWheelDirection = false;
                _panel._historyScrollReady = false;
                _panel._historyJumpPending = false;
                _panel._historyJumpPixels = -1f;
                _panel._selectionCoordinator.ResetForNewRun();
            }

            // Both reset paths discard the current viewport's captured history. Their
            // identity, jump, wheel and selection differences remain explicit at the call site.
            void ResetCommon()
            {
                _panel._scrollOff = 0;
                _panel._wantedScrollOff = 0;
                _panel._scrollPending = false;
                _panel._nextScrollSend = 0f;
                _panel._renderHistoryShift = 0f;
                _panel._historyLastPixels = 0f;
                _panel._historyTopOff = -1;
                _panel._historyBarDragging = false;
                _panel._historyViewReady = false;
                _panel._historyRefreshPending = false;
                _panel._historyWarmed = false;
                _panel._activeHistoryCache = null;
                _panel._historyRestorePending = false;
                _panel._history = new TerminalHistory();
                _panel._historyRequests.Clear();
                _panel._historyCoordinateShift = 0;
                _panel._historyDisplayedFrame = null;
                _panel._historyLiveSeq = -1;
                _panel._historyLiveHistory = -1;
                _panel._historyLiveCols = 0;
                _panel._historyLiveRows = 0;
                _panel._historyLiveAltScreen = false;
            }

            void PruneCaches()
            {
                if (_panel._historyCaches.Count == 0) return;
                List<string> gone = null;
                foreach (var name in _panel._historyCaches.Keys)
                {
                    if (SessionHub.Instance.Get(name) == null)
                        (gone ?? (gone = new List<string>())).Add(name);
                }
                if (gone == null) return;
                foreach (var name in gone) _panel._historyCaches.Remove(name);
            }

            void DrainReplies(SessionHub hub, ScreenBuf live)
            {
                // Drain every reply so a discarded prefetch cannot strand its request id and
                // suppress the exact deep-history fetch needed by the current viewport.
                while (hub.TryScrollScreen(_panel._state.Name, out var sb))
                {
                    HistoryRequest request = new HistoryRequest();
                    bool pending = sb.ScrollRequestId != 0 &&
                        _panel._historyRequests.TryGetValue(sb.ScrollRequestId, out request);
                    bool current = live == null || sb.Seq == live.Seq;
                    _panel.ScrollDebugReply(pending, current);
                    if (!pending) continue;

                    _panel._historyRequests.Remove(sb.ScrollRequestId);
                    int shift = TerminalHistory.CaptureShift(sb, live,
                        _panel._historyCoordinateShift - request.CoordinateShift);
                    _panel._history.Add(sb, live, request.Offset, shift, allowStale: !current);
                    if (sb.History >= 0)
                    {
                        int history = sb.History + shift;
                        if (live != null && live.History >= 0 && live.Seq >= sb.Seq)
                            history = live.History;
                        _panel._historyTopOff = Mathf.Clamp(history, 0, MaxScrollLines);
                        ClampTarget();
                    }
                    else if (sb.Off + shift < request.Offset)
                    {
                        int achieved = sb.Off + shift;
                        if (live != null && live.History >= 0 && live.Seq >= sb.Seq)
                            achieved = live.History;
                        _panel._historyTopOff = Mathf.Max(0, achieved);
                        ClampTarget();
                    }
                    _panel._historyRefreshPending = false;
                }
            }

            void ClampTarget()
            {
                if (_panel._historyTopOff < 0 ||
                    _panel._scrollOff <= _panel._historyTopOff) return;
                _panel._scrollOff = _panel._historyTopOff;
                _panel._historyJumpPending = true;
                _panel._historyJumpOff = _panel._historyTopOff;
                _panel._historyJumpPixels = -1f;
            }
        }
    }
}
