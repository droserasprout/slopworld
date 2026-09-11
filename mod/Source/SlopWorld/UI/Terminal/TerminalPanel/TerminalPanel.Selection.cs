using UnityEngine;

namespace SlopWorld
{

    // TerminalPanel selection state, scrolling, and text extraction.
    sealed partial class TerminalPanel
    {
        // The selection endpoints belong to the displayed history offset. A live frame and a
        // historical frame use the same row coordinates, translated by this offset.
        // Input controllers get only the selection state they need; panel partials use the
        // owned state directly below.
        internal bool HasSelection
        {
            get => _state.Selection.HasSelection;
            set => _state.Selection.HasSelection = value;
        }
        internal bool Dragging
        {
            get => _state.Selection.Dragging;
            set => _state.Selection.Dragging = value;
        }
        internal bool SelectionMoved
        {
            get => _state.Selection.SelectionMoved;
            set => _state.Selection.SelectionMoved = value;
        }
        internal bool MultiClickSelection
        {
            get => _state.Selection.MultiClickSelection;
            set => _state.Selection.MultiClickSelection = value;
        }
        internal bool WordDragging
        {
            get => _state.Selection.WordDragging;
            set => _state.Selection.WordDragging = value;
        }
        internal bool LineDragging
        {
            get => _state.Selection.LineDragging;
            set => _state.Selection.LineDragging = value;
        }
        internal int LineStart
        {
            get => _state.Selection.LineStart;
            set => _state.Selection.LineStart = value;
        }
        internal Vector2Int SelectionA
        {
            get => _state.Selection.A;
            set => _state.Selection.A = value;
        }
        internal Vector2Int SelectionB
        {
            get => _state.Selection.B;
            set => _state.Selection.B = value;
        }
        internal Vector2 SelectionMouse
        {
            get => _state.Selection.Mouse;
            set => _state.Selection.Mouse = value;
        }
        internal int SelectionEdgeDirection
        {
            get => _state.Selection.EdgeDirection;
            set => _state.Selection.EdgeDirection = value;
        }
        internal int SelectionEdgeFrame
        {
            get => _state.Selection.EdgeFrame;
            set => _state.Selection.EdgeFrame = value;
        }

        void NoteLiveFrame(ScreenBuf live, int restoredShift = int.MinValue)
        {
            if (live == null || live.Seq == _selectionCoordinator.LastLiveSeq) return;

            int liveShift = restoredShift == int.MinValue
                ? TerminalHistory.ShiftSince(_historyLiveHistory, live) : restoredShift;
            _selectionCoordinator.NoteLiveFrame(live.Seq, liveShift);
            _historyLiveSeq = live.Seq;
            _historyLiveHistory = live.History;
            _historyLiveCols = live.Cols;
            _historyLiveRows = live.Rows;
            _historyLiveAltScreen = live.AltScreen;
            ScrollDebugLive(live);

            // History offsets are measured from this live bottom. Once the pane changes
            // (most visibly after a sidebar resize/redraw), snapshots captured for the old
            // sequence describe a different coordinate space. New rows moving off the live
            // pane extend the offset by the same amount, keeping the content under the user's
            // eyes anchored instead of pulling the viewport toward new output.
            if (_scrollOff <= 0 && !_historyWarmed) return;
            if (_scrollOff > 0 && liveShift > 0)
            {
                float cellH = TerminalFont.CellH;
                float pixels = _historyScrollReady && cellH > 0.01f
                    ? HistoryOffsetPixels()
                    : _historyJumpPixels >= 0f ? _historyJumpPixels
                    : cellH > 0.01f ? _scrollOff * cellH : -1f;
                _scrollOff = Mathf.Min(MaxScrollLines, _scrollOff + liveShift);
                _historyJumpPending = true;
                _historyJumpOff = _scrollOff;
                _historyJumpPixels = pixels >= 0f && cellH > 0.01f
                    ? Mathf.Min(MaxScrollLines * cellH, pixels + liveShift * cellH)
                    : -1f;
                if (_historyTopOff >= 0)
                    _historyTopOff = Mathf.Min(MaxScrollLines,
                        _historyTopOff + liveShift);
            }

            // Keep history available while a watched agent redraws. In-place refreshes do not
            // change the scrollback coordinate, and detected terminal shifts are translated by
            // TerminalHistory; throwing the cache away on every live frame starves active panes
            // because their next history response is almost always one sequence behind.
            float historyStarted = ScrollDebugTimer();
            bool retained = _history.UpdateLive(live, liveShift);
            ScrollDebugUpdateLive(historyStarted);
            if (retained)
            {
                _historyCoordinateShift = Mathf.Clamp(
                    _historyCoordinateShift + liveShift, 0, MaxScrollLines);
                return;
            }

            _historyRequests.Clear();
            _scrollPending = false;
            _wantedScrollOff = 0;
            _historyCoordinateShift = 0;
            _historyTopOff = -1;
            _historyRefreshPending = true;
            _historyWarmed = false;
            _activeHistoryCache = null;
        }

        void SyncSelectionOffset(int offset)
        {
            _selectionCoordinator.SyncOffset(offset);
        }

        internal void ClearSelection()
        {
            _state.Selection.HasSelection = false;
            _state.Selection.Dragging = false;
            _state.Selection.SelectionMoved = false;
            _state.Selection.MultiClickSelection = false;
            _state.Selection.WordDragging = false;
            _state.Selection.LineDragging = false;
            _state.Selection.EdgeDirection = 0;
            _state.Selection.EdgeFrame = -1;
            ReleaseSelection();
        }

        internal Vector2Int CellAt(Rect body, Vector2 m)
        {
            return _selectionCoordinator.CellAt(body, m);
        }

        ScreenBuf DisplayedBuf() => DisplayedScreen();

        void OrderedSel(out Vector2Int a, out Vector2Int b)
        {
            a = _state.Selection.A;
            b = _state.Selection.B;
            if (b.y < a.y || (b.y == a.y && b.x < a.x)) { var t = a; a = b; b = t; }
        }

        string SelectionText(ScreenBuf buf)
        {
            return _selectionCoordinator.SelectionText(buf);
        }

        // Colors are resolved into the runs at parse time, so a scheme change is a re-parse:
        // without it an idle pane keeps the old palette until the agent next writes, which on
        // an idle agent is never.
        void EnsureRuns(ScreenBuf buf)
            => _renderer.EnsureRuns(buf);

    }
}
