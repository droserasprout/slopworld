using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Daemon history replies are overlapping viewport snapshots. Index their rows in one
    // stable coordinate space (live row = row, history row = row - off) so one prefetched
    // snapshot supplies many local scroll positions instead of becoming one indivisible frame.
    internal sealed class TerminalHistory
    {
        // The client never asks beyond the daemon's 10,000-line history limit, and negotiated
        // panes top out at 200 rows. Bounding coordinates keeps a malformed reply from turning
        // this persistent per-window cache into an unbounded dictionary.
        const int MaxHistoryRows = 10_000;
        const int MaxScreenRows = 200;

        readonly Dictionary<int, string> _lines = new Dictionary<int, string>();
        ScreenBuf _template;
        int _version;
        int _cachedAnchor = -1;
        bool _cachedExtra;
        int _cachedVersion = -1;
        ScreenBuf _cachedView;
        bool _templateAltScreen;

        public void Reset(ScreenBuf live = null)
        {
            _lines.Clear();
            _template = null;
            if (live != null)
            {
                SetTemplate(live);
                Index(live, 0, true);
            }
            Changed();
        }

        // Keep the cache in the newest live pane's coordinate space. A redraw that edits the
        // visible rows leaves history coordinates intact; a terminal scroll moves every old
        // row down by the number of rows that entered at the bottom.
        public bool UpdateLive(ScreenBuf live, int shift)
        {
            if (live == null) return false;
            if (_template != null && !SameViewport(live))
            {
                Reset(live);
                return false;
            }

            if (_template == null)
            {
                Reset(live);
                return false;
            }
            if (shift > 0) Shift(shift);

            SetTemplate(live);
            Index(live, 0, true);
            Changed();
            return true;
        }

        public void Add(ScreenBuf frame, ScreenBuf live, int nearOff,
                        int coordinateShift = 0, bool allowStale = false)
        {
            if (frame == null || frame.Lines == null || frame.Lines.Length == 0) return;
            bool current = live == null || live.Seq == frame.Seq;
            if (!current && !allowStale) return;
            if (live != null && _template != null && !SameViewport(frame)) return;

            if (_template == null)
            {
                if (live != null)
                {
                    SetTemplate(live);
                    Index(live, 0, true);
                }
                else SetTemplate(frame);
            }

            // The overlap at global row zero belongs to the current live frame. An older
            // response can still contribute its negative history rows, but must not overwrite
            // newer content that was redrawn in place while the request was in flight.
            int off = Math.Max(0, frame.Off + coordinateShift);
            Index(frame, off, current);
            Changed();
        }

        public bool TryView(int anchor, bool extraRow, out ScreenBuf view)
        {
            anchor = Math.Max(0, anchor);
            if (_cachedView != null && _cachedAnchor == anchor &&
                _cachedExtra == extraRow && _cachedVersion == _version)
            {
                view = _cachedView;
                return true;
            }

            if (_template == null)
            {
                view = null;
                return false;
            }

            int rows = Math.Max(1, _template.Rows);
            int count = rows + (extraRow ? 1 : 0);
            var lines = new string[count];
            for (int row = 0; row < count; row++)
            {
                int globalRow = row - anchor;
                if (!_lines.TryGetValue(globalRow, out lines[row]))
                {
                    view = null;
                    return false;
                }
            }

            view = new ScreenBuf
            {
                // Blit's cache keys on Seq/Off. Include the row-cache revision because a
                // newly arrived overlapping snapshot can improve this same anchor.
                Seq = unchecked(_template.Seq * 397 ^ _version),
                Cols = _template.Cols,
                Rows = _template.Rows,
                Cx = 0,
                Cy = _template.Rows,
                Off = anchor,
                CursorShape = _template.CursorShape,
                CursorBlink = false,
                AppMouse = false,
                AppDrag = false,
                AltScreen = false,
                Title = _template.Title,
                Lines = lines,
            };
            _cachedAnchor = anchor;
            _cachedExtra = extraRow;
            _cachedVersion = _version;
            _cachedView = view;
            return true;
        }

        // Request planning asks about a lookahead anchor on every GUI pass. Do not build that
        // speculative view: doing so would evict the cached on-screen view and allocate two
        // row arrays per frame.
        public bool Covers(int anchor, bool extraRow)
        {
            anchor = Math.Max(0, anchor);
            if (_template == null) return false;
            int count = Math.Max(1, _template.Rows) + (extraRow ? 1 : 0);
            for (int row = 0; row < count; row++)
                if (!_lines.ContainsKey(row - anchor)) return false;
            return true;
        }

        // Keep lookahead requests on stable overlapping viewport boundaries. A probe derived
        // directly from `target` moves one row per touchpad update and floods the daemon with
        // almost identical captures instead of letting one prefetched frame serve the range.
        public static int PrefetchAnchor(int target, int span, bool up, int max)
        {
            target = Math.Max(0, Math.Min(max, target));
            span = Math.Max(2, span);
            if (up)
            {
                int raw = Math.Min(max, target + span / 2);
                long rounded = ((long)raw + span - 1) / span * span;
                return (int)Math.Min(max, rounded);
            }

            int down = Math.Max(0, target - span / 2);
            return down / span * span;
        }

        bool SameViewport(ScreenBuf frame) => _template != null &&
            (frame.Cols <= 0 || _template.Cols == frame.Cols) &&
            (frame.Rows <= 0 || _template.Rows == frame.Rows) &&
            _templateAltScreen == frame.AltScreen;

        void SetTemplate(ScreenBuf frame)
        {
            _template = new ScreenBuf
            {
                Seq = frame.Seq,
                Cols = frame.Cols,
                Rows = frame.Rows,
                CursorShape = frame.CursorShape,
                Title = frame.Title,
            };
            _templateAltScreen = frame.AltScreen;
        }

        void Index(ScreenBuf frame, int off, bool includeLive)
        {
            if (_template == null) SetTemplate(frame);

            for (int row = 0; row < frame.Lines.Length; row++)
            {
                int globalRow = row - off;
                if (!includeLive && globalRow >= 0) continue;
                if (globalRow < -MaxHistoryRows || globalRow >= MaxScreenRows) continue;
                _lines[globalRow] = frame.Lines[row];
            }
        }

        void Shift(int rows)
        {
            if (rows <= 0 || _lines.Count == 0) return;

            var moved = new Dictionary<int, string>();
            foreach (var pair in _lines)
            {
                int global = pair.Key - rows;
                if (global >= -MaxHistoryRows && global < MaxScreenRows)
                    moved[global] = pair.Value;
            }

            _lines.Clear();
            foreach (var pair in moved) _lines[pair.Key] = pair.Value;
        }

        void Changed()
        {
            _version++;
            _cachedView = null;
            _cachedVersion = -1;
        }
    }
}
