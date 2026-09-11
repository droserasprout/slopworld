using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Redraw scheduling, frame retention across session handoff, and the backing surface.
    sealed partial class TerminalPanel
    {
        float _cursorBlinkAt
        {
            get => _state.CursorBlinkAt;
            set => _state.CursorBlinkAt = value;
        }

        sealed class DisplayedFrameState
        {
            public ScreenBuf Frame;
            public long RunId;
            public int ConnectionGeneration;
        }

        // Keep a complete frame for each tab while its subscription catches up after a switch.
        readonly Dictionary<string, DisplayedFrameState> _displayedFrames =
            new Dictionary<string, DisplayedFrameState>();
        float _renderHistoryShift;

        internal void ResetCursorBlink() => _cursorBlinkAt = Time.realtimeSinceStartup;

        float HistoryShift(ScreenBuf buf, float cellH)
        {
            if (buf == null || _scrollOff <= 0 || !_historyScrollReady ||
                cellH <= 0.01f) return 0f;

            // A refresh keeps the last complete frame on screen while its replacement is in
            // flight. That fallback still belongs to its preceding anchor: translating it from
            // the new requested anchor moves the wrong rows and flashes at the pane edge. Freeze
            // the frame at the shift it was last drawn with, then resume local movement when an
            // assembled view for the current anchor is ready.
            float fallback = Mathf.Abs(_renderHistoryShift) < cellH
                ? _renderHistoryShift : 0f;
            if (!_historyViewReady) return fallback;

            float shift = HistoryOffsetPixels() - buf.Off * cellH;
            return Mathf.Abs(shift) < cellH ? shift : fallback;
        }

        float DisplayedHistoryShift(float cellH) =>
            !_historyScrollReady || cellH <= 0.01f ? 0f : _renderHistoryShift;

        ScreenBuf CachedDisplayedFrame(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!_displayedFrames.TryGetValue(name, out var state)) return null;
            var info = SessionHub.Instance.Get(name);
            if (info == null || state.RunId != info.RunId ||
                state.ConnectionGeneration != SessionHub.Instance.ConnectionGeneration)
            {
                _displayedFrames.Remove(name);
                return null;
            }
            return state.Frame;
        }

        void RememberDisplayedFrame(string name, ScreenBuf frame)
        {
            if (string.IsNullOrEmpty(name) || frame == null || frame.Lines == null ||
                frame.Lines.Length == 0)
                return;

            var info = SessionHub.Instance.Get(name);
            if (info == null) return;
            if (_displayedFrames.TryGetValue(name, out var previous) &&
                previous.Frame.Seq == frame.Seq && previous.Frame.Off == frame.Off &&
                previous.Frame.Cols == frame.Cols && previous.Frame.Rows == frame.Rows &&
                previous.RunId == info.RunId &&
                previous.ConnectionGeneration == SessionHub.Instance.ConnectionGeneration)
                return;

            _displayedFrames[name] = new DisplayedFrameState
            {
                Frame = frame.Snapshot(),
                RunId = info.RunId,
                ConnectionGeneration = SessionHub.Instance.ConnectionGeneration,
            };
        }

        internal static Color SolidTerminalBackground
        {
            get
            {
                var c = Sgr.DefaultBg;
                c.a = 1f;
                return c;
            }
        }

        // Background faces overlap their boundary by one screen pixel. The terminal cache
        // and IMGUI clipping can each round an edge in the opposite direction, and a face
        // that only reaches the nominal rect can therefore expose a hairline seam.
        internal static Rect OverdrawBackground(Rect r)
        {
            float p = Slab.LineW;
            return new Rect(r.x - p, r.y - p, r.width + p * 2f, r.height + p * 2f);
        }

        internal void Update()
        {

            // Both of these are the pane's business with its own session, and a view in the
            // body means there is no pane being measured or scrolled.
            if (_state.Name == null || !Visible) return;

            float now = Time.realtimeSinceStartup;
            if (_scrollPending && now >= _nextScrollSend)
                SendPendingScroll();

            if (!_state.SizeDirty || now < _state.ResizeAt) return;

            // A socket that is down drops the message, so hold the ask rather than spend it.
            if (!SessionHub.Instance.Online)
            {
                _state.ResizeAt = now + 1f;
                return;
            }

            SessionHub.Instance.Terminal.Resize(_state.Name, _state.Cols, _state.Rows);
            _state.SizeDirty = false;
        }

    }
}
