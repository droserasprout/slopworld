using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Daemon history replies are overlapping viewport snapshots. Index their rows in one
    // stable coordinate space (live row = row, history row = row - off) so one prefetched
    // snapshot supplies many local scroll positions instead of becoming one indivisible frame.
    internal sealed class TerminalHistory
    {
        const int MaxFrames = 32;

        readonly Dictionary<int, ScreenBuf> _frames = new Dictionary<int, ScreenBuf>();
        int _seq = -1;
        int _version;
        int _cachedAnchor = -1;
        bool _cachedExtra;
        int _cachedVersion = -1;
        ScreenBuf _cachedView;

        public void Reset(ScreenBuf live = null)
        {
            _frames.Clear();
            _seq = live?.Seq ?? -1;
            if (live != null)
            {
                var snapshot = live.Snapshot();
                snapshot.Off = 0;
                _frames[0] = snapshot;
            }
            Changed();
        }

        public void Add(ScreenBuf frame, ScreenBuf live, int nearOff)
        {
            if (frame == null || frame.Lines == null || frame.Lines.Length == 0) return;
            // An offset is relative to the live bottom captured with the frame. Mixing it
            // with a newer live sequence produces a plausible but wrong bridge, and is
            // especially easy to do when a resize causes output while a scroll request is
            // still in flight.
            if (live != null && live.Seq != frame.Seq) return;

            if (_seq != frame.Seq)
            {
                _frames.Clear();
                _seq = frame.Seq;
                if (live != null && live.Seq == frame.Seq)
                {
                    var snapshot = live.Snapshot();
                    snapshot.Off = 0;
                    _frames[0] = snapshot;
                }
            }

            if (_frames.TryGetValue(frame.Off, out var old) && ReferenceEquals(old, frame)) return;
            _frames[frame.Off] = frame;
            Prune(nearOff);
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

            var primary = Primary(anchor);
            if (primary == null)
            {
                view = null;
                return false;
            }

            int rows = Math.Max(1, primary.Rows);
            int count = rows + (extraRow ? 1 : 0);
            var lines = new string[count];
            for (int row = 0; row < count; row++)
            {
                int globalRow = row - anchor;
                if (!TryLine(globalRow, anchor, out lines[row]))
                {
                    view = null;
                    return false;
                }
            }

            view = new ScreenBuf
            {
                // Blit's cache keys on Seq/Off. Include the row-cache revision because a
                // newly arrived overlapping snapshot can improve this same anchor.
                Seq = unchecked(primary.Seq * 397 ^ _version),
                Cols = primary.Cols,
                Rows = primary.Rows,
                Cx = 0,
                Cy = primary.Rows,
                Off = anchor,
                CursorShape = primary.CursorShape,
                CursorBlink = false,
                AppMouse = false,
                AppDrag = false,
                AltScreen = false,
                Title = primary.Title,
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
            var primary = Primary(anchor);
            if (primary == null) return false;
            int count = Math.Max(1, primary.Rows) + (extraRow ? 1 : 0);
            for (int row = 0; row < count; row++)
                if (!TryLine(row - anchor, anchor, out _)) return false;
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

        ScreenBuf Primary(int anchor)
        {
            ScreenBuf best = null;
            int distance = int.MaxValue;
            foreach (var frame in _frames.Values)
            {
                if (frame.Seq != _seq || frame.Lines == null) continue;
                int row = frame.Off - anchor;
                if (row < 0 || row >= frame.Lines.Length) continue;
                int d = Math.Abs(frame.Off - anchor);
                if (d >= distance) continue;
                best = frame;
                distance = d;
            }
            return best;
        }

        bool TryLine(int globalRow, int anchor, out string line)
        {
            ScreenBuf best = null;
            int bestRow = -1;
            int distance = int.MaxValue;
            foreach (var frame in _frames.Values)
            {
                if (frame.Seq != _seq || frame.Lines == null) continue;
                int row = globalRow + frame.Off;
                if (row < 0 || row >= frame.Lines.Length) continue;
                int d = Math.Abs(frame.Off - anchor);
                if (d >= distance) continue;
                best = frame;
                bestRow = row;
                distance = d;
            }

            line = bestRow >= 0 ? best.Lines[bestRow] : null;
            return bestRow >= 0;
        }

        void Prune(int nearOff)
        {
            while (_frames.Count > MaxFrames)
            {
                int remove = -1;
                int distance = -1;
                foreach (var off in _frames.Keys)
                {
                    if (off == 0 || off == nearOff) continue;
                    int d = Math.Abs(off - nearOff);
                    if (d <= distance) continue;
                    remove = off;
                    distance = d;
                }
                if (remove < 0) break;
                _frames.Remove(remove);
            }
        }

        void Changed()
        {
            _version++;
            _cachedView = null;
            _cachedVersion = -1;
        }
    }
}
