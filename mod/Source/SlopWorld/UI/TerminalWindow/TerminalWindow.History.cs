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
        // A shallow history window is kept ready while the active pane is at the live bottom,
        // making the first wheel movement a local transition instead of a capture round trip.
        bool _historyWarmed;

        readonly TerminalHistory _history = new TerminalHistory();

        struct HistoryRequest
        {
            public int Offset;
            public int CoordinateShift;
            public bool Warm;

            public HistoryRequest(int offset, int coordinateShift, bool warm = false)
            {
                Offset = offset;
                CoordinateShift = coordinateShift;
                Warm = warm;
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
            if (_scrollOff <= 0) WarmHistory(live);
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
                        _historyTopOff = Mathf.Clamp(sb.History + shift, 0, MaxScrollLines);
                        if (!request.Warm) ClampHistoryTarget();
                    }
                    else if (sb.Off + shift < request.Offset)
                    {
                        // Older daemons do not report the history extent. Their achieved
                        // offset is still authoritative when the requested point was above
                        // the real top.
                        _historyTopOff = Mathf.Max(0, sb.Off + shift);
                        if (!request.Warm) ClampHistoryTarget();
                    }
                    _historyRefreshPending = false;
                }
            }
        }

        void WarmHistory(ScreenBuf live)
        {
            if (_historyWarmed || live == null || live.Lines == null || live.Lines.Length == 0 ||
                !SessionHub.Instance.Online || _sizeDirty || _scrollPending ||
                _historyRequests.Count > 0 || !HistoryInputEnabled(live))
                return;
            if (_cols > 0 && _rows > 0 && (live.Cols != _cols || live.Rows != _rows)) return;

            _history.Reset(live);
            _historyCoordinateShift = 0;
            _historyTopOff = -1;
            _historyWarmed = true;

            int offset = Mathf.Max(2, live.Rows / 2);
            ulong id = ++_nextScrollRequestId;
            _historyRequests[id] = new HistoryRequest(offset, 0, warm: true);
            SessionHub.Instance.RequestScroll(_name, offset, id);
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
