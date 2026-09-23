using System;
using System.Collections.Generic;
using System.Text;

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
        static int MaxHistoryRows => Math.Max(1, Math.Min(
            TerminalLimits.ClientMaxScrollbackLines,
            DaemonCapabilities.Current.Terminal.ScrollbackLines));
        static int MaxScreenRows => Math.Max(1, Math.Min(
            TerminalLimits.ClientMaxRows, DaemonCapabilities.Current.Terminal.MaxRows));

        // Keys stay fixed while the live bottom advances. `_origin` translates the public
        // coordinate space to storage coordinates, avoiding a dictionary-sized copy for every
        // line the watched terminal scrolls.
        readonly Dictionary<int, string> _lines = new Dictionary<int, string>();
        int _origin;
        public int Count => _lines.Count;
        public int Cols => _template?.Cols ?? 0;
        public bool TryLine(int globalRow, out string line) =>
            _lines.TryGetValue(Storage(globalRow), out line);

        // Copy the entire range, including rows outside the displayed viewport. Refuse an
        // incomplete range rather than silently publishing truncated text to the clipboard.
        public string SelectionText(ScreenBuf displayed, int ax, int ay, int bx, int by)
        {
            if (by < ay || (by == ay && bx < ax))
            {
                int x = ax, y = ay;
                ax = bx; ay = by; bx = x; by = y;
            }
            var text = new StringBuilder();
            for (int row = ay; row <= by; row++)
            {
                string line;
                if (row >= 0 && row < displayed.Lines.Length) line = displayed.Lines[row];
                else if (!TryLine(row - displayed.Off, out line)) return "";
                var cells = TerminalColumns.Cells(Sgr.ParseLine(line));
                int len = TerminalColumns.ContentColumns(cells);
                int start = row == ay ? Math.Max(0, ax) : 0;
                int end = row == by ? Math.Min(bx + 1, len) : len;
                if (end > start) text.Append(TerminalColumns.Slice(cells, start, end - 1));
                if (row < by) text.Append('\n');
            }
            return text.ToString();
        }
        public int Rows => _template?.Rows ?? 0;
        public bool AltScreen => _templateAltScreen;

        // Several streamed frames can arrive between panel draws. The most recent frame's
        // LiveShift covers only its predecessor, not the cache's last observation.
        public static int ShiftSince(int previousHistory, ScreenBuf live) =>
            previousHistory >= 0 && live.History >= 0 && live.History < MaxHistoryRows
                ? Math.Max(0, live.History - previousHistory) : live.LiveShift;

        // Live metadata is authoritative even before warmup, and after an application
        // clears history. Only older daemons need a capture to discover the scroll limit.
        public static int ScrollLimit(ScreenBuf live, int knownTop)
        {
            int extent = live != null && live.History >= 0 ? live.History : knownTop;
            return extent < 0 ? MaxHistoryRows : Math.Min(MaxHistoryRows, extent);
        }

        // Fill overlapping windows progressively, nearest first. The caller sends only one
        // request at a time, so a gesture can replace speculative work after the next reply.
        public int WarmupOffset(ScreenBuf live, int knownTop)
        {
            if (live == null || live.AltScreen || live.AppMouse || live.Off != 0 ||
                live.Rows < 2 || live.Cols <= 0 || live.Lines == null ||
                live.Lines.Length == 0) return 0;
            int extent = ScrollLimit(live, knownTop);
            if (extent == 0) return 0;
            int offset = PrefetchOffset(0, live.Rows, true, extent);
            return Math.Max(0, offset);
        }

        public int PrefetchOffset(int target, int rows, bool up, int max)
        {
            max = Math.Max(0, Math.Min(MaxHistoryRows, max));
            if (max == 0) return -1;
            int span = Math.Max(1, Math.Min(MaxScreenRows, rows) / 2);
            target = Math.Max(0, Math.Min(max, target));
            int boundary = up ? target / span * span : (target + span - 1) / span * span;
            // Sixteen half-viewport windows cover roughly eight screens ahead. Checking the
            // fractional edge preserves a bridge between each pair of captures.
            for (int step = 1; step <= 16; step++)
            {
                int offset = up ? Math.Min(max, boundary + step * span)
                    : Math.Max(1, boundary - step * span);
                if (!Covers(offset, true)) return offset;
                if (up ? offset == max : offset == 1) break;
            }
            return -1;
        }
        // The displayed scrollback snapshot includes live-tail rows. Keep all of it fixed
        // while streaming, even when background captures refresh the indexed row cache.
        ScreenBuf _pinnedView;
        bool _pinnedExtra;
        public void ReleaseView() => _pinnedView = null;

        ScreenBuf _template;
        int _version;
        int _cachedAnchor = -1;
        bool _cachedExtra;
        int _cachedVersion = -1;
        ScreenBuf _cachedView;
        bool _templateAltScreen;

        public void Reset(ScreenBuf live = null)
        {
            ReleaseView();
            _lines.Clear();
            _origin = 0;
            _template = null;
            if (live != null)
            {
                SetTemplate(live);
                Index(live, 0, true);
            }
            Changed();
        }

        // Keep the cache in the newest live pane's coordinate space. A redraw that edits the
        // visible rows leaves history coordinates intact. A terminal scroll moves every old
        // row down by the number of rows that entered at the bottom.
        public bool UpdateLive(ScreenBuf live, int shift)
        {
            if (live == null) return false;
            if (_template != null && (!SameViewport(live) ||
                (live.History >= 0 && _template.History > live.History)))
            {
                Reset(live);
                return false;
            }

            if (_template == null)
            {
                Reset(live);
                return false;
            }
            if (shift > 0)
            {
                // A TUI may rewrite its prompt before scrolling between streamed frames.
                // Those old live rows are not evidence of what entered daemon history.
                // Leave holes for authoritative captures rather than caching phantom text.
                if (live.History >= 0)
                    for (int row = 0; row < Math.Min(shift, Rows); row++)
                        _lines.Remove(Storage(row));
                Shift(shift);
                if (_pinnedView != null) _pinnedView.Off += shift;
            }

            SetTemplate(live);
            bool changed = Index(live, 0, true);
            // Shallow views still contain live rows, including the fractional overscan row.
            // Refresh those in-place edits without discarding a history-only view. A real
            // scroll can preserve that deep view by translating its anchor instead.
            bool historyOnly = _cachedView != null &&
                _cachedAnchor >= _cachedView.Lines.Length;
            if (shift > 0 && historyOnly)
            {
                _cachedAnchor += shift;
                _cachedView.Off += shift;
            }
            else if (changed && (shift > 0 || !historyOnly)) Changed();
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

            // The overlap at global row zero belongs to the current live frame. An older response
            // can still contribute its negative history rows. However, Must not overwrite newer
            // content that was redrawn in place while the request was in flight.
            int off = Math.Max(0, frame.Off + CaptureShift(frame, live, coordinateShift));
            if (Index(frame, off, current)) Changed();
        }

        // Off belongs to the daemon's capture time, not the time we sent the request.
        // Output before capture is already represented in the reply. Translating it again
        // overwrites neighboring cached rows and makes input/answer lines disappear.
        public static int CaptureShift(ScreenBuf frame, ScreenBuf live, int requestShift)
        {
            if (live == null || frame.Seq == live.Seq) return 0;
            if (frame.History >= 0 && live.History >= 0)
                return Math.Max(0, live.History - frame.History);
            return requestShift;
        }

        public bool TryView(int anchor, bool extraRow, out ScreenBuf view, bool freeze = false)
        {
            anchor = Math.Max(0, anchor);
            if (freeze && _pinnedView != null && _pinnedView.Off == anchor &&
                _pinnedExtra == extraRow)
            {
                view = _pinnedView;
                return true;
            }
            if (_cachedView != null && _cachedAnchor == anchor &&
                _cachedExtra == extraRow && _cachedVersion == _version)
            {
                view = _cachedView;
                SetViewCursor(view);
                if (freeze) { PinView(view, extraRow); view = _pinnedView; }
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
                if (!_lines.TryGetValue(Storage(globalRow), out lines[row]))
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
            SetViewCursor(view);
            if (freeze) { PinView(view, extraRow); view = _pinnedView; }
            return true;
        }

        void PinView(ScreenBuf view, bool extraRow)
        {
            _pinnedView = new ScreenBuf
            {
                Seq = view.Seq,
                Off = view.Off,
                Rows = view.Rows,
                Cols = view.Cols,
                Lines = view.Lines,
                Cy = view.Lines.Length,
                Title = view.Title,
            };
            _pinnedExtra = extraRow;
        }

        // Cursor updates do not invalidate row textures. Translate only a visible live
        // cursor. Cy == Rows is the daemon's hidden-cursor sentinel.
        void SetViewCursor(ScreenBuf view)
        {
            view.Cx = _template.Cx;
            view.Cy = _template.Cy >= 0 && _template.Cy < _template.Rows
                ? _template.Cy + view.Off : view.Lines.Length;
            view.CursorShape = _template.CursorShape;
            view.CursorBlink = _template.CursorBlink;
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
                if (!_lines.ContainsKey(Storage(row - anchor))) return false;
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
                History = frame.History,
                Cols = frame.Cols,
                Rows = frame.Rows,
                Cx = frame.Cx,
                Cy = frame.Cy,
                CursorShape = frame.CursorShape,
                CursorBlink = frame.CursorBlink,
                Title = frame.Title,
            };
            _templateAltScreen = frame.AltScreen;
        }

        bool Index(ScreenBuf frame, int off, bool includeLive)
        {
            if (_template == null) SetTemplate(frame);
            bool changed = false;

            for (int row = 0; row < frame.Lines.Length; row++)
            {
                int globalRow = row - off;
                // A delayed reply's live tail may since have been rewritten and scrolled.
                // Translation cannot turn that old prompt into authoritative history.
                if (!includeLive && (globalRow >= 0 || row >= frame.Off)) continue;
                if (globalRow < -MaxHistoryRows || globalRow >= MaxScreenRows) continue;
                int key = Storage(globalRow);
                string line = frame.Lines[row];
                if (!_lines.TryGetValue(key, out var old) || old != line)
                {
                    _lines[key] = line;
                    changed = true;
                }
            }
            return changed;
        }

        void Shift(int rows)
        {
            if (rows <= 0 || _lines.Count == 0) return;
            _origin += rows;

            // Only the coordinate slots that crossed the fixed lower bound can expire. Remove
            // those directly: no full dictionary scan, copy, or temporary collection.
            for (int delta = 1; delta <= rows; delta++)
                _lines.Remove(Storage(-MaxHistoryRows - delta));
        }

        int Storage(int global) => global + _origin;

        void Changed()
        {
            _version++;
            _cachedView = null;
            _cachedVersion = -1;
        }
    }
}
