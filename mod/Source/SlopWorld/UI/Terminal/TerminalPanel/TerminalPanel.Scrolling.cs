using UnityEngine;

namespace SlopWorld
{
    // Local scroll position, fractional motion, and serialized history requests.
    sealed partial class TerminalPanel
    {
        // Mouse-wheel scrollback: lines scrolled up from the live bottom.
        int _scrollOff;
        // Leading-edge throttle: the first request sends immediately, then the rest ride the
        // display beat. The wanted offset is the newest prefetch target and the sent offset
        // is what was last sent. Request ids retain their own offsets until answered, so a
        // delayed prefetch can populate the row cache without clamping a newer gesture.
        int _wantedScrollOff;
        int _sentScrollOff;
        float _nextScrollSend;
        bool _scrollPending;
        bool _lastWheelUp;
        bool _hasWheelDirection;
        // SessionStore keeps the last reply by session, including while a window is closed.
        // A process-wide id prevents a newly opened window's first request from colliding
        // with that retained reply.
        static ulong _nextScrollRequestId;
        const float ScrollBeat = 1f / 60f;

        // The daemon owns terminal history, but the visible position is local so a touchpad
        // can move between snapshots without waiting for a websocket round trip.
        readonly SmoothScroll _historyScroll = new SmoothScroll();
        bool _historyScrollReady;
        bool _historyJumpPending;
        int _historyJumpOff;
        float _historyJumpPixels = -1f;
        float _historyMax;
        float _historyLastPixels;
        int _historyConnectionGeneration = -1;
        bool _historyBarDragging;
        float _historyBarGrab;

        // A socket reconnect starts a new daemon screen stream. Keep the current visual
        // fallback, but discard request bookkeeping and indexed rows so a response or sequence
        // from the previous connection cannot satisfy the new terminal's history view.
        void SyncHistoryConnection()
        {
            int generation = SessionHub.Instance.ConnectionGeneration;
            if (_historyConnectionGeneration == generation) return;
            _historyConnectionGeneration = generation;

            _historyCaches.Clear();
            _activeHistoryCache = null;
            _historyRestorePending = false;
            _historyRequests.Clear();
            _scrollPending = false;
            _wantedScrollOff = 0;
            _sentScrollOff = 0;
            _nextScrollSend = 0f;
            _hasWheelDirection = false;
            _historyCoordinateShift = 0;
            _historyTopOff = -1;
            _historyViewReady = false;
            _historyRefreshPending = false;
            _historyWarmed = false;
            _historyBarDragging = false;
            _history = new TerminalHistory();
            _selectionCoordinator.ResetLiveSequence();
            _historyLiveSeq = -1;
            _historyLiveHistory = -1;
            _historyLiveCols = 0;
            _historyLiveRows = 0;
            _historyLiveAltScreen = false;
        }

        internal void JumpToLive()
        {
            _scrollOff = 0;
            _wantedScrollOff = 0;
            _scrollPending = false;
            _nextScrollSend = 0f;
            _hasWheelDirection = false;
            // Input is allowed to leave history immediately. Waiting for the next draw pass
            // to apply `_historyJumpPending` leaves `UpdateHistoryTarget` looking at the old
            // wheel position and can put the pane straight back into the frozen snapshot.
            if (_historyScrollReady)
                _historyScroll.JumpTo(new Vector2(0f, _historyMax));
            _historyLastPixels = 0f;
            JumpHistoryTo(0);
            ResetCursorBlink();
            ScrollDebugEnd();
        }

        internal void QueueScroll(bool up, bool fromLive = false, int requestOff = -1)
        {
            float now = Time.realtimeSinceStartup;
            ScrollDebugInput();
            _wantedScrollOff = requestOff >= 0 ? requestOff : _scrollOff;
            _scrollPending = true;
            ScrollDebugQueued(_wantedScrollOff);
            bool fresh = fromLive || !_hasWheelDirection || up != _lastWheelUp ||
                now >= _nextScrollSend;
            _lastWheelUp = up;
            _hasWheelDirection = true;
            if (fresh) SendPendingScroll();
        }

        void PrepareHistoryScroll(float cellH, ScreenBuf live)
        {
            if (cellH <= 0.01f) return;

            float max = cellH * TerminalHistory.ScrollLimit(live, _historyTopOff);
            if (!_historyScrollReady || Mathf.Abs(_historyMax - max) > 0.01f)
            {
                // Growing history moves the bottom coordinate. Keep the reader's offset
                // from that bottom, rather than resetting it whenever the extent changes.
                float pixels = _historyScrollReady ? HistoryOffsetPixels() : 0f;
                _historyMax = max;
                _historyScroll.JumpTo(new Vector2(0f, Mathf.Max(0f, max - pixels)));
                _historyScrollReady = true;
            }

            if (_historyJumpPending)
            {
                float pixels = _historyJumpPixels >= 0f
                    ? _historyJumpPixels : _historyJumpOff * cellH;
                _historyScroll.JumpTo(new Vector2(0f,
                    Mathf.Clamp(_historyMax - pixels, 0f, _historyMax)));
                _historyJumpPending = false;
                _historyJumpPixels = -1f;
            }
        }

        float HistoryOffsetPixels() =>
            Mathf.Clamp(_historyMax - _historyScroll.Position.y, 0f, _historyMax);

        // Retain the fractional local position while fetching overlapping whole-line windows.
        void UpdateHistoryTarget(float cellH, ScreenBuf live)
        {
            if (!_historyScrollReady || cellH <= 0.01f) return;

            float pixels = HistoryOffsetPixels();
            if (Mathf.Abs(pixels - _historyLastPixels) > 0.01f)
                ScrollDebugInput();
            int target = pixels <= 0.01f
                ? 0
                : Mathf.Clamp(Mathf.CeilToInt(pixels / cellH - 0.0001f), 1, MaxScrollLines);
            int limit = TerminalHistory.ScrollLimit(live, _historyTopOff);
            if (target > limit)
            {
                target = limit;
                JumpHistoryTo(target);
            }

            int previous = _scrollOff;
            bool up = pixels > _historyLastPixels + 0.01f ? true
                : pixels < _historyLastPixels - 0.01f ? false
                : target >= previous;
            _historyLastPixels = pixels;
            bool fromLive = previous <= 0 && target > 0;
            if (fromLive)
            {
                if (!_historyWarmed)
                {
                    _history.Reset(live);
                    _historyRequests.Clear();
                    _historyCoordinateShift = 0;
                    _historyTopOff = live != null && live.History >= 0 ? live.History : -1;
                }
                _historyDisplayedFrame = live?.Snapshot();
                _historyWarmed = true;
            }

            if (target == 0)
            {
                if (previous > 0)
                {
                    _historyDisplayedFrame = null;
                }
                // There is no reason to send a coalesced prefetch after the live view has
                // become authoritative. An already-running request may still drain and seed
                // the warm cache. Cancel only the unsent request.
                _scrollPending = false;
                _wantedScrollOff = 0;
                _scrollOff = 0;
                ScrollDebugEnd();
                WarmHistory(live);
                return;
            }

            _scrollOff = target;

            // Fetch missing visible rows first, then progressively fill eight viewports in
            // the gesture direction. Each capture overlaps the preceding cached window.
            int rows = Mathf.Max(2, live?.Rows ?? (_state.Rows > 0 ? _state.Rows : 24));
            int lookahead = Mathf.Max(2, rows / 2);
            int probe = TerminalHistory.PrefetchAnchor(
                target, lookahead, up, limit);
            bool fractional = Mathf.Abs(pixels / cellH - Mathf.Round(pixels / cellH)) > 0.0001f;
            int request = -1;
            if (!_history.Covers(target, fractional))
            {
                // Near the live edge, one lookahead frame overlaps live and covers both jobs.
                // A larger leap needs its exact viewport first. If that viewport is present but
                // lacks the fractional edge row, fetch a newer bridge into the cached range.
                if (fromLive && target <= lookahead) request = probe;
                else if (!_history.Covers(target, false)) request = target;
                else request = Mathf.Max(1, target - lookahead);
            }
            else
                request = _history.PrefetchOffset(target, rows, up, limit);

            // A new live frame makes the old snapshots stale, but they are still the best
            // frame to show until this offset has been captured again. Request a replacement
            // without tearing down the visible bridge.
            if (_historyRefreshPending && !HistoryRequestPending(target))
                request = target;

            if (request > 0 && !HistoryRequestPending(request))
                QueueScroll(up, fromLive, request);
        }

        bool HistoryRequestPending(int off)
        {
            foreach (var requested in _historyRequests.Values)
                if (requested.Offset == off) return true;
            return _scrollPending && _wantedScrollOff == off;
        }

        void WarmHistory(ScreenBuf live)
        {
            var hub = SessionHub.Instance;
            if (!hub.Online || _state.SizeDirty || IsEditorSession() ||
                _historyRequests.Count > 0 || live == null ||
                (_state.Cols > 0 && live.Cols != _state.Cols) ||
                (_state.Rows > 0 && live.Rows != _state.Rows))
                return;

            int offset = _history.WarmupOffset(live, _historyTopOff);
            if (offset <= 0) return;
            if (!_historyWarmed)
            {
                _history.Reset(live);
                _historyCoordinateShift = 0;
                _historyWarmed = true;
            }
            // Use capture bookkeeping without QueueScroll's gesture/direction state. Neither
            // the local offset nor the displayed live frame changes when the reply arrives.
            _wantedScrollOff = offset;
            _scrollPending = true;
            SendPendingScroll();
        }

        internal void JumpHistoryTo(int off)
        {
            off = Mathf.Clamp(off, 0,
                TerminalHistory.ScrollLimit(SessionHub.Instance.Screen(_state.Name), _historyTopOff));
            _historyJumpPending = true;
            _historyJumpOff = off;
            _historyJumpPixels = -1f;
            float cellH = TerminalFont.CellH;
            if (_historyScrollReady && cellH > 0.01f)
            {
                float pixels = Mathf.Min(_historyMax, off * cellH);
                _historyScroll.JumpTo(new Vector2(0f, _historyMax - pixels));
                _historyLastPixels = pixels;
            }
        }


        void SendPendingScroll()
        {
            if (!_scrollPending) return;

            // The daemon reads this socket sequentially and awaits each capture. More than one
            // in flight turns fast touchpad movement into a FIFO of obsolete viewports ahead of
            // the current target. Keep the newest wanted offset pending until this reply drains.
            if (_historyRequests.Count > 0) return;

            _scrollPending = false;

            if (_wantedScrollOff <= 0) return;

            _sentScrollOff = _wantedScrollOff;
            _nextScrollSend = Time.realtimeSinceStartup + ScrollBeat;

            ulong id = ++_nextScrollRequestId;
            _historyRequests[id] = new HistoryRequest(
                _sentScrollOff, _historyCoordinateShift);
            SessionHub.Instance.Terminal.RequestScroll(_state.Name, _sentScrollOff, id);
            ScrollDebugSent(_sentScrollOff);
        }
    }
}
