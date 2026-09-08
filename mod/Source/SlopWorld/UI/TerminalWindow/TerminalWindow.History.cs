using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Terminal history snapshots, request replies, and the displayed historical frame.
    public partial class TerminalWindow
    {
        // History coordinates are owned by the daemon, but the window keeps overlapping
        // snapshots so fractional scrolling can be served locally.
        const int MaxScrollLines = 10_000;

        struct ScrollbackState
        {
            public int Offset;
            public float Pixels;
            public long RunId;

            public ScrollbackState(int offset, float pixels, long runId)
            {
                Offset = offset;
                Pixels = pixels;
                RunId = runId;
            }
        }

        sealed class HistoryCacheState
        {
            public TerminalHistory History;
            public int TopOffset;
            public int CoordinateShift;
            public bool Warmed;
            public bool RefreshPending;
            public long RunId;
            public int ConnectionGeneration;
            public int LiveSeq;
            public int LiveHistory;
            public int Cols;
            public int Rows;
            public bool AltScreen;
        }

        readonly Dictionary<string, ScrollbackState> _scrollbackStates =
            new Dictionary<string, ScrollbackState>();
        // Inactive tabs retain their indexed rows, but not pending requests. A subscription
        // clears the daemon's reply queue, so requests must be planned again when the tab
        // returns; the rows themselves can still satisfy the restored viewport immediately.
        readonly Dictionary<string, HistoryCacheState> _historyCaches =
            new Dictionary<string, HistoryCacheState>();
        HistoryCacheState _activeHistoryCache;

        bool _historyViewReady;
        bool _historyRefreshPending;
        int _historyTopOff = -1;
        // The history cache is seeded only when the user leaves the live bottom, so merely
        // opening or revisiting a tab never issues a scroll capture.
        bool _historyWarmed;

        TerminalHistory _history = new TerminalHistory();
        bool _historyRestorePending;
        int _historyLiveSeq = -1;
        int _historyLiveHistory = -1;
        int _historyLiveCols;
        int _historyLiveRows;
        bool _historyLiveAltScreen;

        struct HistoryRequest
        {
            public int Offset;
            public int CoordinateShift;

            public HistoryRequest(int offset, int coordinateShift)
            {
                Offset = offset;
                CoordinateShift = coordinateShift;
            }
        }

        readonly Dictionary<ulong, HistoryRequest> _historyRequests =
            new Dictionary<ulong, HistoryRequest>();
        // Total live rows translated since the current history cache was seeded. Requests
        // captured before a live terminal scroll need the same translation before indexing.
        int _historyCoordinateShift;
        // Stable fallback while the first prefetched window for a new position is in flight.
        ScreenBuf _historyDisplayedFrame;

        ScreenBuf DisplayedScreen()
        {
            var hub = SessionHub.Instance;
            var live = hub.Screen(_name);
            NoteLiveFrame(live);
            DrainHistoryReplies(hub, live);
            if (_scrollOff <= 0)
            {
                _historyViewReady = false;
                SyncSelectionOffset(0);
                return live;
            }

            float cellH = TerminalFont.CellH;
            float lines = cellH > 0.01f ? HistoryOffsetPixels() / cellH : _scrollOff;
            int anchor = Mathf.Max(0, Mathf.CeilToInt(lines - 0.0001f));
            bool extra = Mathf.Abs(lines - Mathf.Round(lines)) > 0.0001f;
            float viewStarted = ScrollDebugTimer();
            _historyViewReady = _history.TryView(anchor, extra, out var displayed);
            ScrollDebugTryView(viewStarted);
            ScrollDebugDisplayable(anchor, displayed, _historyViewReady);
            displayed = displayed ?? _historyDisplayedFrame ?? live;
            if (displayed != null) _historyDisplayedFrame = displayed;
            if (displayed != null) SyncSelectionOffset(displayed.Off);
            return displayed;
        }

        void SaveHistoryCache(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            PruneHistoryCaches();
            var info = SessionHub.Instance.Get(name);
            if (info == null) return;

            _historyCaches[name] = new HistoryCacheState
            {
                History = _history,
                TopOffset = _historyTopOff,
                CoordinateShift = _historyCoordinateShift,
                Warmed = _historyWarmed,
                RefreshPending = _historyRefreshPending,
                RunId = info.RunId,
                ConnectionGeneration = SessionHub.Instance.ConnectionGeneration,
                LiveSeq = _historyLiveSeq,
                LiveHistory = _historyLiveHistory,
                Cols = _historyLiveCols > 0 ? _historyLiveCols : _history.Cols,
                Rows = _historyLiveRows > 0 ? _historyLiveRows : _history.Rows,
                AltScreen = _historyLiveSeq >= 0 ? _historyLiveAltScreen : _history.AltScreen,
            };
        }

        void PruneHistoryCaches()
        {
            if (_historyCaches.Count == 0) return;
            List<string> gone = null;
            foreach (var name in _historyCaches.Keys)
            {
                if (SessionHub.Instance.Get(name) == null)
                    (gone ?? (gone = new List<string>())).Add(name);
            }
            if (gone == null) return;
            foreach (var name in gone) _historyCaches.Remove(name);
        }

        void RestoreHistoryCache(string name)
        {
            _activeHistoryCache = null;
            _historyRestorePending = false;
            if (string.IsNullOrEmpty(name) ||
                !_historyCaches.TryGetValue(name, out var cache))
                return;

            var info = SessionHub.Instance.Get(name);
            if (info == null || cache.RunId != info.RunId ||
                cache.ConnectionGeneration != SessionHub.Instance.ConnectionGeneration)
            {
                _historyCaches.Remove(name);
                return;
            }

            _activeHistoryCache = cache;
            _history = cache.History ?? new TerminalHistory();
            _historyTopOff = cache.TopOffset;
            _historyCoordinateShift = cache.CoordinateShift;
            _historyWarmed = cache.Warmed;
            _historyRefreshPending = cache.RefreshPending;
            _historyLiveSeq = cache.LiveSeq;
            _historyLiveHistory = cache.LiveHistory;
            _historyLiveCols = cache.Cols;
            _historyLiveRows = cache.Rows;
            _historyLiveAltScreen = cache.AltScreen;
            _historyRestorePending = true;
        }

        // A switched tab has no comparable live frame until its subscription catches up. Once
        // it arrives, translate the retained rows by the daemon's history growth and reject the
        // cache if the run, viewport, or terminal screen mode no longer matches.
        int RestoredHistoryShift(ScreenBuf live)
        {
            if (!_historyRestorePending || live == null) return int.MinValue;
            _historyRestorePending = false;

            var info = SessionHub.Instance.Get(_name);
            bool compatible = info != null && _activeHistoryCache != null &&
                _activeHistoryCache.RunId == info.RunId &&
                _activeHistoryCache.ConnectionGeneration == SessionHub.Instance.ConnectionGeneration &&
                (_historyLiveCols <= 0 || live.Cols <= 0 || _historyLiveCols == live.Cols) &&
                (_historyLiveRows <= 0 || live.Rows <= 0 || _historyLiveRows == live.Rows) &&
                _historyLiveAltScreen == live.AltScreen;

            int shift = 0;
            if (compatible && _historyLiveSeq >= 0 && live.Seq == _historyLiveSeq)
            {
                // A same-sequence subscription replay can hydrate metadata without adding
                // scrollback rows. Treat it as unchanged even if the history field differs.
                compatible = _historyLiveHistory < 0 || live.History < 0 ||
                    live.History == _historyLiveHistory;
            }
            else if (compatible && _historyLiveHistory >= 0 && live.History >= 0)
            {
                shift = live.History - _historyLiveHistory;
                compatible = shift >= 0;
            }
            else if (compatible && _historyLiveSeq >= 0 && live.Seq != _historyLiveSeq)
            {
                // Without both history extents there is no safe way to translate rows that
                // may have scrolled while the tab was inactive.
                compatible = false;
            }

            if (compatible) return shift;

            _activeHistoryCache = null;
            _history = new TerminalHistory();
            _historyTopOff = -1;
            _historyCoordinateShift = 0;
            _historyWarmed = false;
            _historyRefreshPending = false;
            _historyDisplayedFrame = null;
            return 0;
        }

        void DrainHistoryReplies(SessionHub hub, ScreenBuf live)
        {
            // Several prefetched replies can arrive during one Unity frame. Drain all of
            // them: replacing one reply with the next strands the discarded request id in
            // `_historyRequests`, which can suppress the exact deep-history fetch now needed.
            while (hub.TryScrollScreen(_name, out var sb))
            {
                HistoryRequest request = new HistoryRequest();
                bool pending = sb.ScrollRequestId != 0 &&
                    _historyRequests.TryGetValue(sb.ScrollRequestId, out request);
                bool current = live == null || sb.Seq == live.Seq;
                ScrollDebugReply(pending, current);
                if (pending)
                {
                    _historyRequests.Remove(sb.ScrollRequestId);
                    int shift = _historyCoordinateShift - request.CoordinateShift;
                    _history.Add(sb, live, request.Offset, shift, allowStale: !current);
                    if (sb.History >= 0)
                    {
                        // The reply's extent is translated for live rows that arrived while
                        // the capture was in flight. Once the live frame is at least as new as
                        // that reply, its daemon-owned extent is authoritative. In particular,
                        // do not turn a zero-history warm reply plus a stale one-row shift into
                        // a phantom history row after returning to a tab.
                        int history = sb.History + shift;
                        if (live != null && live.History >= 0 && live.Seq >= sb.Seq)
                            history = live.History;
                        _historyTopOff = Mathf.Clamp(history, 0, MaxScrollLines);
                        ClampHistoryTarget();
                    }
                    else if (sb.Off + shift < request.Offset)
                    {
                        // Older daemons do not report the history extent. Their achieved
                        // offset is still authoritative when the requested point was above
                        // the real top.
                        int achieved = sb.Off + shift;
                        if (live != null && live.History >= 0 && live.Seq >= sb.Seq)
                            achieved = live.History;
                        _historyTopOff = Mathf.Max(0, achieved);
                        ClampHistoryTarget();
                    }
                    _historyRefreshPending = false;
                }
            }
        }

        void ClampHistoryTarget()
        {
            if (_historyTopOff < 0 || _scrollOff <= _historyTopOff) return;
            _scrollOff = _historyTopOff;
            _historyJumpPending = true;
            _historyJumpOff = _historyTopOff;
            _historyJumpPixels = -1f;
        }

        bool HistoryInputEnabled(ScreenBuf live) =>
            _scrollOff > 0 || live == null ||
                (!live.AppMouse && !live.AltScreen && !IsEditorSession());

        // A durable session name can be reused for a new process. Do not let the new
        // emulator inherit the old run's indexed rows, request ids, or fractional position.
        // The stopped branch calls this before the replacement process publishes its first
        // frame, so the first frame is treated as a new live bottom even when the tab stays
        // open throughout a restart/auto-resume.
        internal void ResetHistoryForNewRun()
        {
            if (_lastLiveSeq < 0 && !_historyWarmed && _historyRequests.Count == 0 &&
                _scrollOff == 0)
                return;

            _lastLiveSeq = -1;
            _historyCaches.Remove(_name);
            _scrollbackStates.Remove(_name);
            _activeHistoryCache = null;
            _history = new TerminalHistory();
            _historyRequests.Clear();
            _scrollPending = false;
            _wantedScrollOff = 0;
            _sentScrollOff = 0;
            _nextScrollSend = 0f;
            _scrollOff = 0;
            _historyJumpPending = true;
            _historyJumpOff = 0;
            _historyJumpPixels = -1f;
            _historyLastPixels = 0f;
            _renderHistoryShift = 0f;
            _historyCoordinateShift = 0;
            _historyTopOff = -1;
            _historyBarDragging = false;
            _historyViewReady = false;
            _historyRefreshPending = false;
            _historyWarmed = false;
            _historyDisplayedFrame = null;
            _historyRestorePending = false;
            _historyLiveSeq = -1;
            _historyLiveHistory = -1;
            _historyLiveCols = 0;
            _historyLiveRows = 0;
            _historyLiveAltScreen = false;
            _selectionOff = 0;
            ClearSelection();
        }

        bool IsEditorSession()
        {
            var info = SessionHub.Instance.Get(_name);
            if (info == null) return false;
            if (Pager.IsEditorCommand(info.Cmd)) return true;
            return info.Ephemeral &&
                (info.Name ?? "").StartsWith("edit-", System.StringComparison.Ordinal);
        }
    }
}
