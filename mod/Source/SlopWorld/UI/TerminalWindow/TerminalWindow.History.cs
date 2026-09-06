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

            public ScrollbackState(int offset, float pixels)
            {
                Offset = offset;
                Pixels = pixels;
            }
        }

        readonly Dictionary<string, ScrollbackState> _scrollbackStates =
            new Dictionary<string, ScrollbackState>();

        bool _historyViewReady;
        bool _historyRefreshPending;
        int _historyTopOff = -1;
        // The history cache is seeded only when the user leaves the live bottom, so merely
        // opening or revisiting a tab never issues a scroll capture.
        bool _historyWarmed;

        readonly TerminalHistory _history = new TerminalHistory();

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
            _history.Reset();
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
