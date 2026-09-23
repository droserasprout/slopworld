using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Terminal history snapshots, request replies, and the displayed historical frame.
    sealed partial class TerminalPanel
    {
        // History coordinates are owned by the daemon, but the window keeps overlapping
        // snapshots so fractional scrolling can be served locally.
        int MaxScrollLines => Math.Max(1, Math.Min(
            TerminalLimits.ClientMaxScrollbackLines,
            SessionHub.Instance.Capabilities.Terminal.ScrollbackLines));

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
        // returns. The rows themselves can still satisfy the restored viewport immediately.
        readonly Dictionary<string, HistoryCacheState> _historyCaches =
            new Dictionary<string, HistoryCacheState>();
        HistoryCacheState _activeHistoryCache;

        bool _historyViewReady;
        bool _historyRefreshPending;
        int _historyTopOff = -1;
        // Seed once per compatible history epoch, either by active-pane warmup or a gesture.
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

            public HistoryRequest(int offset)
            {
                Offset = offset;
            }
        }

        readonly Dictionary<ulong, HistoryRequest> _historyRequests =
            new Dictionary<ulong, HistoryRequest>();
        // Stable fallback while the first prefetched window for a new position is in flight.
        ScreenBuf _historyDisplayedFrame;

        ScreenBuf DisplayedScreen() => _historyCoordinator.DisplayedScreen();

        int RestoredHistoryShift(ScreenBuf live) => _historyCoordinator.RestoredShift(live);

        bool HistoryInputEnabled(ScreenBuf live) => _historyCoordinator.InputEnabled(live);

        // A durable session name can be reused for a new process. Do not let the new emulator
        // inherit the old run's indexed rows, request ids, or fractional position. The stopped
        // branch calls this before the replacement process publishes its first frame. Therefore,
        // The first frame is treated as a new live bottom even when the tab stays open throughout a
        // restart/auto-resume.
        internal void ResetHistoryForNewRun() => _historyCoordinator.ResetForNewRun();

        bool IsEditorSession()
        {
            var info = SessionHub.Instance.Get(_state.Name);
            if (info == null) return false;
            if (Pager.IsEditorCommand(info.Cmd)) return true;
            return info.Ephemeral &&
                (info.Name ?? "").StartsWith("edit-", System.StringComparison.Ordinal);
        }
    }
}
