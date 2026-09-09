using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    public partial class TerminalWindow
    {
        // History owns daemon-offset translation and stale-reply decisions. Scrolling keeps the
        // local fractional gesture; this owner decides which immutable frame represents it.
        sealed class TerminalHistoryCoordinator
        {
            readonly TerminalWindow _window;

            public TerminalHistoryCoordinator(TerminalWindow window)
            {
                _window = window;
            }

            public ScreenBuf DisplayedScreen()
            {
                var hub = SessionHub.Instance;
                var live = hub.Screen(_window._name);
                _window.NoteLiveFrame(live);
                DrainReplies(hub, live);
                if (_window._scrollOff <= 0)
                {
                    _window._historyViewReady = false;
                    _window.SyncSelectionOffset(0);
                    return live;
                }

                float cellH = TerminalFont.CellH;
                float lines = cellH > 0.01f
                    ? _window.HistoryOffsetPixels() / cellH : _window._scrollOff;
                int anchor = Mathf.Max(0, Mathf.CeilToInt(lines - 0.0001f));
                bool extra = Mathf.Abs(lines - Mathf.Round(lines)) > 0.0001f;
                float viewStarted = _window.ScrollDebugTimer();
                _window._historyViewReady = _window._history.TryView(
                    anchor, extra, out var displayed);
                _window.ScrollDebugTryView(viewStarted);
                _window.ScrollDebugDisplayable(anchor, displayed, _window._historyViewReady);
                displayed = displayed ?? _window._historyDisplayedFrame ?? live;
                if (displayed != null) _window._historyDisplayedFrame = displayed;
                if (displayed != null) _window.SyncSelectionOffset(displayed.Off);
                return displayed;
            }

            public void SaveCache(string name)
            {
                if (string.IsNullOrEmpty(name)) return;
                PruneCaches();
                var info = SessionHub.Instance.Get(name);
                if (info == null) return;

                _window._historyCaches[name] = new HistoryCacheState
                {
                    History = _window._history,
                    TopOffset = _window._historyTopOff,
                    CoordinateShift = _window._historyCoordinateShift,
                    Warmed = _window._historyWarmed,
                    RefreshPending = _window._historyRefreshPending,
                    RunId = info.RunId,
                    ConnectionGeneration = SessionHub.Instance.ConnectionGeneration,
                    LiveSeq = _window._historyLiveSeq,
                    LiveHistory = _window._historyLiveHistory,
                    Cols = _window._historyLiveCols > 0
                        ? _window._historyLiveCols : _window._history.Cols,
                    Rows = _window._historyLiveRows > 0
                        ? _window._historyLiveRows : _window._history.Rows,
                    AltScreen = _window._historyLiveSeq >= 0
                        ? _window._historyLiveAltScreen : _window._history.AltScreen,
                };
            }

            public void RestoreCache(string name)
            {
                _window._activeHistoryCache = null;
                _window._historyRestorePending = false;
                if (string.IsNullOrEmpty(name) ||
                    !_window._historyCaches.TryGetValue(name, out var cache))
                    return;

                var info = SessionHub.Instance.Get(name);
                if (info == null || cache.RunId != info.RunId ||
                    cache.ConnectionGeneration != SessionHub.Instance.ConnectionGeneration)
                {
                    _window._historyCaches.Remove(name);
                    return;
                }

                _window._activeHistoryCache = cache;
                _window._history = cache.History ?? new TerminalHistory();
                _window._historyTopOff = cache.TopOffset;
                _window._historyCoordinateShift = cache.CoordinateShift;
                _window._historyWarmed = cache.Warmed;
                _window._historyRefreshPending = cache.RefreshPending;
                _window._historyLiveSeq = cache.LiveSeq;
                _window._historyLiveHistory = cache.LiveHistory;
                _window._historyLiveCols = cache.Cols;
                _window._historyLiveRows = cache.Rows;
                _window._historyLiveAltScreen = cache.AltScreen;
                _window._historyRestorePending = true;
            }

            public int RestoredShift(ScreenBuf live)
            {
                if (!_window._historyRestorePending || live == null) return int.MinValue;
                _window._historyRestorePending = false;

                var info = SessionHub.Instance.Get(_window._name);
                bool compatible = info != null && _window._activeHistoryCache != null &&
                    _window._activeHistoryCache.RunId == info.RunId &&
                    _window._activeHistoryCache.ConnectionGeneration ==
                        SessionHub.Instance.ConnectionGeneration &&
                    (_window._historyLiveCols <= 0 || live.Cols <= 0 ||
                        _window._historyLiveCols == live.Cols) &&
                    (_window._historyLiveRows <= 0 || live.Rows <= 0 ||
                        _window._historyLiveRows == live.Rows) &&
                    _window._historyLiveAltScreen == live.AltScreen;

                int shift = 0;
                if (compatible && _window._historyLiveSeq >= 0 &&
                    live.Seq == _window._historyLiveSeq)
                {
                    compatible = _window._historyLiveHistory < 0 || live.History < 0 ||
                        live.History == _window._historyLiveHistory;
                }
                else if (compatible && _window._historyLiveHistory >= 0 && live.History >= 0)
                {
                    shift = live.History - _window._historyLiveHistory;
                    compatible = shift >= 0;
                }
                else if (compatible && _window._historyLiveSeq >= 0 &&
                         live.Seq != _window._historyLiveSeq)
                {
                    compatible = false;
                }

                if (compatible) return shift;

                _window._activeHistoryCache = null;
                _window._history = new TerminalHistory();
                _window._historyTopOff = -1;
                _window._historyCoordinateShift = 0;
                _window._historyWarmed = false;
                _window._historyRefreshPending = false;
                _window._historyDisplayedFrame = null;
                return 0;
            }

            public bool InputEnabled(ScreenBuf live) =>
                _window._scrollOff > 0 || live == null ||
                (!live.AppMouse && !live.AltScreen && !_window.IsEditorSession());

            public void ResetForNewRun()
            {
                if (_window._selectionCoordinator.LastLiveSeq < 0 &&
                    !_window._historyWarmed && _window._historyRequests.Count == 0 &&
                    _window._scrollOff == 0)
                    return;

                _window._selectionCoordinator.ResetLiveSequence();
                _window._historyCaches.Remove(_window._name);
                _window._scrollbackStates.Remove(_window._name);
                _window._activeHistoryCache = null;
                _window._history = new TerminalHistory();
                _window._historyRequests.Clear();
                _window._scrollPending = false;
                _window._wantedScrollOff = 0;
                _window._sentScrollOff = 0;
                _window._nextScrollSend = 0f;
                _window._scrollOff = 0;
                _window._historyJumpPending = true;
                _window._historyJumpOff = 0;
                _window._historyJumpPixels = -1f;
                _window._historyLastPixels = 0f;
                _window._renderHistoryShift = 0f;
                _window._historyCoordinateShift = 0;
                _window._historyTopOff = -1;
                _window._historyBarDragging = false;
                _window._historyViewReady = false;
                _window._historyRefreshPending = false;
                _window._historyWarmed = false;
                _window._historyDisplayedFrame = null;
                _window._historyRestorePending = false;
                _window._historyLiveSeq = -1;
                _window._historyLiveHistory = -1;
                _window._historyLiveCols = 0;
                _window._historyLiveRows = 0;
                _window._historyLiveAltScreen = false;
                _window._selectionCoordinator.ResetForNewRun();
                _window.ClearSelection();
            }

            public void ResetForSession()
            {
                _window._scrollOff = 0;
                _window._wantedScrollOff = 0;
                _window._scrollPending = false;
                _window._nextScrollSend = 0f;
                _window._hasWheelDirection = false;
                _window._historyScrollReady = false;
                _window._historyJumpPending = false;
                _window._historyJumpPixels = -1f;
                _window._renderHistoryShift = 0f;
                _window._historyLastPixels = 0f;
                _window._historyTopOff = -1;
                _window._historyBarDragging = false;
                _window._historyViewReady = false;
                _window._historyRefreshPending = false;
                _window._historyWarmed = false;
                _window._activeHistoryCache = null;
                _window._historyRestorePending = false;
                _window._history = new TerminalHistory();
                _window._historyRequests.Clear();
                _window._historyCoordinateShift = 0;
                _window._historyDisplayedFrame = null;
                _window._historyLiveSeq = -1;
                _window._historyLiveHistory = -1;
                _window._historyLiveCols = 0;
                _window._historyLiveRows = 0;
                _window._historyLiveAltScreen = false;
                _window._selectionCoordinator.ResetForNewRun();
            }

            void PruneCaches()
            {
                if (_window._historyCaches.Count == 0) return;
                List<string> gone = null;
                foreach (var name in _window._historyCaches.Keys)
                {
                    if (SessionHub.Instance.Get(name) == null)
                        (gone ?? (gone = new List<string>())).Add(name);
                }
                if (gone == null) return;
                foreach (var name in gone) _window._historyCaches.Remove(name);
            }

            void DrainReplies(SessionHub hub, ScreenBuf live)
            {
                // Drain every reply so a discarded prefetch cannot strand its request id and
                // suppress the exact deep-history fetch needed by the current viewport.
                while (hub.TryScrollScreen(_window._name, out var sb))
                {
                    HistoryRequest request = new HistoryRequest();
                    bool pending = sb.ScrollRequestId != 0 &&
                        _window._historyRequests.TryGetValue(sb.ScrollRequestId, out request);
                    bool current = live == null || sb.Seq == live.Seq;
                    _window.ScrollDebugReply(pending, current);
                    if (!pending) continue;

                    _window._historyRequests.Remove(sb.ScrollRequestId);
                    int shift = TerminalHistory.CaptureShift(sb, live,
                        _window._historyCoordinateShift - request.CoordinateShift);
                    _window._history.Add(sb, live, request.Offset, shift, allowStale: !current);
                    if (sb.History >= 0)
                    {
                        int history = sb.History + shift;
                        if (live != null && live.History >= 0 && live.Seq >= sb.Seq)
                            history = live.History;
                        _window._historyTopOff = Mathf.Clamp(history, 0, MaxScrollLines);
                        ClampTarget();
                    }
                    else if (sb.Off + shift < request.Offset)
                    {
                        int achieved = sb.Off + shift;
                        if (live != null && live.History >= 0 && live.Seq >= sb.Seq)
                            achieved = live.History;
                        _window._historyTopOff = Mathf.Max(0, achieved);
                        ClampTarget();
                    }
                    _window._historyRefreshPending = false;
                }
            }

            void ClampTarget()
            {
                if (_window._historyTopOff < 0 ||
                    _window._scrollOff <= _window._historyTopOff) return;
                _window._scrollOff = _window._historyTopOff;
                _window._historyJumpPending = true;
                _window._historyJumpOff = _window._historyTopOff;
                _window._historyJumpPixels = -1f;
            }
        }
    }
}
