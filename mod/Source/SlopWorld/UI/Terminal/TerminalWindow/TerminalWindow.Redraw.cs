using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Redraw scheduling, frame retention across session handoff, and the backing surface.
    public partial class TerminalWindow
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

        // The window is also the host for settings and other content views. Those views are
        // SlopWorld chrome, so their fullscreen backing surface belongs to UIScheme; only the
        // pane itself is allowed to expose the terminal palette here.
        static Color SolidTerminalBackground
        {
            get
            {
                var c = Sgr.DefaultBg;
                c.a = 1f;
                return c;
            }
        }

        Color Background => TerminalVisible ? SolidTerminalBackground : UiWidgets.WindowBg;

        // Background faces overlap their boundary by one screen pixel. The terminal cache
        // and IMGUI clipping can each round an edge in the opposite direction, and a face
        // that only reaches the nominal rect can therefore expose a hairline seam.
        static Rect OverdrawBackground(Rect r)
        {
            float p = Slab.LineW;
            return new Rect(r.x - p, r.y - p, r.width + p * 2f, r.height + p * 2f);
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();

            // Both of these are the pane's business with its own session, and a view in the
            // body means there is no pane being measured or scrolled.
            if (_name == null || !TerminalVisible) return;

            float now = Time.realtimeSinceStartup;
            if (_scrollPending && now >= _nextScrollSend)
                SendPendingScroll();

            if (!_sizeDirty || now < _resizeAt) return;

            // A socket that is down drops the message, so hold the ask rather than spend it.
            if (!SessionHub.Instance.Online)
            {
                _resizeAt = now + 1f;
                return;
            }

            SessionHub.Instance.Terminal.Resize(_name, _cols, _rows);
            _sizeDirty = false;
        }

        public override void ExtraOnGUI()
        {
            base.ExtraOnGUI();
            if (Event.current.type != EventType.Repaint) return;

            // UI.screenWidth truncates, so the window is a fraction of a pixel short of the
            // right edge and nothing covers or repaints the last column, PaneOverDraw having
            // stood the map down. Here rather than by widening the window, which would be a
            // wider pane.
            Slab.Fill(OverdrawBackground(new Rect(0f, 0f,
                Mathf.Ceil(Screen.width / Prefs.UIScale),
                Mathf.Ceil(Screen.height / Prefs.UIScale))), Background);

            Flush();
        }
    }
}
