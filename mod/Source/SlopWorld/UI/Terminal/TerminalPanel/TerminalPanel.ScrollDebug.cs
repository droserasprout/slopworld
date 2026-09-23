using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Aggregate scroll timings for one terminal window. Enable with
    // SLOPWORLD_DEBUG=1 in the game's environment. The normal path does no timing or
    // string work beyond a few predictable branches.
    sealed partial class TerminalPanel
    {
        static readonly bool ScrollDebugEnabled = PerfTrace.Enabled;

        struct ScrollDebugState
        {
            public bool Active;
            public float StartedAt, NextReport;
            public float FirstQueued, FirstSent, FirstReply, FirstDisplayable;
            public int LastInputFrame;
            public int Inputs, Queued, Sent, Replies, StaleReplies, Displayable;
            public int RepaintFrames, Paints, Blits;
            public int RowCacheHits, RowCacheMisses;
            public float UpdateLiveMs, TryViewMs, ParseMs, PaintMs, BlitMs;
            public int Anchor, Target, Requested, Seq, Rows, Cols, LiveShift;
            public float OffsetPixels;
            public int LastDisplayAnchor, LastDisplaySeq;
        }

        ScrollDebugState _scrollDebug;

        static float ScrollDebugNow() => Time.realtimeSinceStartup;

        float ScrollDebugTimer() => ScrollDebugEnabled ? ScrollDebugNow() : -1f;

        void ScrollDebugInput()
        {
            if (!ScrollDebugEnabled) return;
            float now = ScrollDebugNow();
            if (!_scrollDebug.Active)
            {
                _scrollDebug = new ScrollDebugState
                {
                    Active = true,
                    StartedAt = now,
                    NextReport = now + 1f,
                    FirstQueued = -1f,
                    FirstSent = -1f,
                    FirstReply = -1f,
                    FirstDisplayable = -1f,
                    LastInputFrame = -1,
                    Anchor = -1,
                    Requested = -1,
                    Seq = -1,
                    LastDisplayAnchor = -1,
                    LastDisplaySeq = int.MinValue,
                };
            }

            if (_scrollDebug.LastInputFrame == Time.frameCount) return;
            _scrollDebug.LastInputFrame = Time.frameCount;
            _scrollDebug.Inputs++;
        }

        void ScrollDebugQueued(int offset)
        {
            if (!ScrollDebugEnabled) return;
            ScrollDebugInput();
            float now = ScrollDebugNow();
            if (_scrollDebug.FirstQueued < 0f) _scrollDebug.FirstQueued = now;
            _scrollDebug.Queued++;
            _scrollDebug.Requested = offset;
        }

        void ScrollDebugSent(int offset)
        {
            if (!ScrollDebugEnabled) return;
            float now = ScrollDebugNow();
            if (_scrollDebug.FirstSent < 0f) _scrollDebug.FirstSent = now;
            _scrollDebug.Sent++;
            _scrollDebug.Requested = offset;
        }

        void ScrollDebugReply(bool pending, bool current)
        {
            if (!ScrollDebugEnabled || !_scrollDebug.Active) return;
            float now = ScrollDebugNow();
            if (_scrollDebug.FirstReply < 0f) _scrollDebug.FirstReply = now;
            _scrollDebug.Replies++;
            if (pending && !current) _scrollDebug.StaleReplies++;
        }

        void ScrollDebugDisplayable(int anchor, ScreenBuf view, bool ready)
        {
            if (!ScrollDebugEnabled || !_scrollDebug.Active || !ready) return;
            int seq = view?.Seq ?? -1;
            if (_scrollDebug.LastDisplayAnchor == anchor && _scrollDebug.LastDisplaySeq == seq)
                return;

            float now = ScrollDebugNow();
            if (_scrollDebug.FirstDisplayable < 0f) _scrollDebug.FirstDisplayable = now;
            _scrollDebug.LastDisplayAnchor = anchor;
            _scrollDebug.LastDisplaySeq = seq;
            _scrollDebug.Displayable++;
        }

        void ScrollDebugUpdateLive(float started)
        {
            if (!ScrollDebugEnabled || started < 0f || !_scrollDebug.Active) return;
            _scrollDebug.UpdateLiveMs += (ScrollDebugNow() - started) * 1000f;
        }

        void ScrollDebugTryView(float started)
        {
            if (!ScrollDebugEnabled || started < 0f || !_scrollDebug.Active) return;
            _scrollDebug.TryViewMs += (ScrollDebugNow() - started) * 1000f;
        }

        void ScrollDebugParse(float started, int hits, int misses)
        {
            if (!ScrollDebugEnabled || started < 0f || !_scrollDebug.Active) return;
            _scrollDebug.ParseMs += (ScrollDebugNow() - started) * 1000f;
            _scrollDebug.RowCacheHits += hits;
            _scrollDebug.RowCacheMisses += misses;
        }

        void ScrollDebugPaint(float started)
        {
            if (!ScrollDebugEnabled || started < 0f || !_scrollDebug.Active) return;
            _scrollDebug.Paints++;
            _scrollDebug.PaintMs += (ScrollDebugNow() - started) * 1000f;
        }

        void ScrollDebugBlit(float started, bool drawn)
        {
            if (!ScrollDebugEnabled || started < 0f || !_scrollDebug.Active) return;
            if (drawn) _scrollDebug.Blits++;
            _scrollDebug.BlitMs += (ScrollDebugNow() - started) * 1000f;
        }

        void ScrollDebugLive(ScreenBuf live)
        {
            if (!ScrollDebugEnabled || !_scrollDebug.Active || live == null) return;
            _scrollDebug.LiveShift = live.LiveShift;
            _scrollDebug.Seq = live.Seq;
            _scrollDebug.Rows = live.Rows;
            _scrollDebug.Cols = live.Cols;
        }

        void ScrollDebugFrame(ScreenBuf buf, float offsetPixels)
        {
            if (!ScrollDebugEnabled || !_scrollDebug.Active) return;
            if (Event.current.type == EventType.Repaint) _scrollDebug.RepaintFrames++;
            _scrollDebug.Anchor = buf?.Off ?? -1;
            _scrollDebug.OffsetPixels = offsetPixels;
            _scrollDebug.Target = _scrollOff;
            _scrollDebug.Requested = _wantedScrollOff;
            _scrollDebug.Seq = buf?.Seq ?? -1;
            _scrollDebug.Rows = buf?.Rows ?? 0;
            _scrollDebug.Cols = buf?.Cols ?? 0;
            ScrollDebugMaybeReport(false);
        }

        void ScrollDebugMaybeReport(bool final)
        {
            if (!ScrollDebugEnabled || !_scrollDebug.Active) return;
            float now = ScrollDebugNow();
            if (!final && now < _scrollDebug.NextReport) return;
            _scrollDebug.NextReport = now + 1f;

            string phase = final ? "end" : "sample";
            Log.Message(
                $"[SlopWorld] scroll-debug {phase} session={_state.Name} " +
                $"age={(now - _scrollDebug.StartedAt) * 1000f:F1}ms " +
                $"queue={Ms(_scrollDebug.StartedAt, _scrollDebug.FirstQueued)} " +
                $"send={Ms(_scrollDebug.FirstQueued, _scrollDebug.FirstSent)} " +
                $"reply={Ms(_scrollDebug.FirstSent, _scrollDebug.FirstReply)} " +
                $"display={Ms(_scrollDebug.FirstReply, _scrollDebug.FirstDisplayable)}ms " +
                $"counts(in={_scrollDebug.Inputs},queued={_scrollDebug.Queued}," +
                $"sent={_scrollDebug.Sent},reply={_scrollDebug.Replies}," +
                $"stale={_scrollDebug.StaleReplies},display={_scrollDebug.Displayable}," +
                $"repaint={_scrollDebug.RepaintFrames},paint={_scrollDebug.Paints}," +
                $"blit={_scrollDebug.Blits},rowHit={_scrollDebug.RowCacheHits}," +
                $"rowMiss={_scrollDebug.RowCacheMisses}) " +
                $"timing(update={_scrollDebug.UpdateLiveMs:F1},view={_scrollDebug.TryViewMs:F1}," +
                $"parse={_scrollDebug.ParseMs:F1},paint={_scrollDebug.PaintMs:F1}," +
                $"blit={_scrollDebug.BlitMs:F1}ms) " +
                $"state(anchor={_scrollDebug.Anchor},target={_scrollDebug.Target}," +
                $"requested={_scrollDebug.Requested},seq={_scrollDebug.Seq}," +
                $"size={_scrollDebug.Cols}x{_scrollDebug.Rows}," +
                $"offset={_scrollDebug.OffsetPixels:F1},liveShift={_scrollDebug.LiveShift}," +
                $"historyRows={_history.Count},pending={_historyRequests.Count + (_scrollPending ? 1 : 0)}," +
                $"parsedRows={_renderer.RunCount},noCache={_noCache})");

            if (final) _scrollDebug.Active = false;
        }

        static string Ms(float from, float to)
        {
            return from < 0f || to < 0f || to < from
                ? "-"
                : ((to - from) * 1000f).ToString("F1");
        }

        void ScrollDebugEnd()
        {
            ScrollDebugMaybeReport(true);
        }
    }
}
