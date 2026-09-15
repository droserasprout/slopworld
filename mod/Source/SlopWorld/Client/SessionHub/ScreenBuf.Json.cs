using System.Collections.Generic;

namespace SlopWorld
{
    public partial class ScreenBuf
    {
        List<int> _changedRows;
        int[] _overlapPrefix = NoChangedRows;
        public void FromJson(JVal s)
        {
            string[] previousLines = Lines;
            int previousRows = Rows;
            int previousCols = Cols;
            int previousCy = Cy;
            int previousHistory = History;
            bool previousAltScreen = AltScreen;
            int previousSeq = Seq;
            var previousRuns = Runs;
            int previousRunsRev = RunsRev;
            bool previousRunsComplete = RunsComplete;
            bool previousHasLinks = HasLinks;

            Seq = s["seq"].AsInt();
            Cols = s["cols"].AsInt(80);
            Rows = s["rows"].AsInt(24);
            Cx = s["cx"].AsInt();
            Cy = s["cy"].AsInt();
            Off = s["off"].AsInt(0);
            History = s["history"].AsInt(-1);
            CursorShape = s["cursor_shape"].AsInt(0);
            CursorBlink = s["cursor_blink"].AsBool(true);
            AppMouse = s["app_mouse"].AsBool(false);
            AppDrag = s["app_drag"].AsBool(false);
            AltScreen = s["alt_screen"].AsBool(false);
            Title = s["title"].AsString();
            ScrollRequestId = (ulong)s["request_id"].AsLong(0);
            var values = s["lines"];
            bool sameShape = previousLines != null && previousLines.Length == values.Count &&
                previousCols == Cols && previousRows == Rows;
            var nextLines = sameShape ? previousLines : new string[values.Count];
            var changed = _changedRows ?? (_changedRows = new List<int>());
            changed.Clear();
            for (int i = 0; i < values.Count; i++)
            {
                string line = values[i].AsString();
                if (sameShape && string.Equals(previousLines[i], line,
                                              System.StringComparison.Ordinal)) continue;
                // Keep old strings/arrays intact for scroll detection and retained views.
                if (sameShape && changed.Count == 0) nextLines = (string[])previousLines.Clone();
                nextLines[i] = line;
                changed.Add(i);
            }

            Lines = nextLines;
            bool contentChanged = !sameShape || changed.Count > 0;
            if (ContentRevision == 0) ContentRevision = 1;
            else if (contentChanged) ContentRevision++;
            ChangedRows = changed.Count == 0 ? NoChangedRows : changed.ToArray();

            // Keep parsed rows whose source text did not change. Changed rows are left null so
            // the terminal parser can fill only those rows on the next draw.
            if (sameShape && previousRuns != null && previousRuns.Length == nextLines.Length)
            {
                // Snapshots share this array, so copy only when invalidating rows.
                var retained = changed.Count == 0 ? previousRuns :
                    (List<SgrRun>[])previousRuns.Clone();
                foreach (int row in changed) retained[row] = null;
                Runs = retained;
                RunsRev = previousRunsRev;
                RunsComplete = previousRunsComplete && changed.Count == 0;
            }
            else
            {
                Runs = null;
                AutoLinks = null;
                BaseRuns = null;
                BaseRunsRev = -1;
                RunsRev = -1;
                RunsComplete = false;
            }

            // Link knowledge describes the last parse, not the last received frame. A replay
            // before the next draw must not mark an unparsed edit as already inspected.
            HasLinks = previousHasLinks;
            LinksKnown = LinksKnown && !contentChanged;
            // A subscription replay can update the retained buffer without advancing the
            // daemon sequence. It is a new observation of the same live epoch, not rows that
            // scrolled while this tab was away; only a strictly newer frame may move history.
            bool newer = previousSeq >= 0 && Seq > previousSeq;
            int visibleShift = Off == 0 && newer &&
                !previousAltScreen && !AltScreen
                ? VerticalShift(previousLines, Lines, previousRows, Rows, previousCy)
                : 0;
            // `-1` means the daemon did not provide a history extent. Learning that an
            // otherwise unchanged live frame has zero history is metadata hydration, not one
            // row of terminal scrollback. This matters when a tab is subscribed again and its
            // retained frame is updated by the first reply from the current daemon.
            int historyShift = Off == 0 && newer &&
                !previousAltScreen && !AltScreen && previousHistory >= 0 &&
                History > previousHistory
                ? History - previousHistory : 0;
            // Visible-row overlap is precise for small shifts, but it cannot identify a
            // burst that scrolls an entire viewport. The daemon's live history extent supplies
            // that missing signal while the buffer still has room to grow; once full, the
            // overlap detector remains the fallback.
            // Before the history limit, its extent is authoritative even when unchanged.
            // A TUI repaint can match a suffix of blank/prompt rows without scrolling.
            LiveShift = previousHistory >= 0 && History >= 0 &&
                History < WireContract.ScrollbackLines ? historyShift :
                System.Math.Max(visibleShift, historyShift);
        }

        internal int VerticalShift(string[] before, string[] after,
            int beforeRows, int afterRows, int beforeCy)
        {
            if (before == null || after == null || beforeRows < 2 || beforeRows != afterRows ||
                before.Length != after.Length || before.Length < beforeRows ||
                beforeCy < beforeRows - 1 || before[beforeRows - 1] == after[afterRows - 1])
                return 0;

            // A real terminal scroll leaves the old tail at the new top. The incoming bottom
            // must differ, and some row above it must move; otherwise an edit to a repeated
            // line ("", for example) looks like a scroll.
            bool changedBeforeBottom = false;
            for (int row = 0; row < afterRows - 1; row++)
            {
                if (before[row] != after[row])
                {
                    changedBeforeBottom = true;
                    break;
                }
            }

            if (!changedBeforeBottom) return 0;

            // Match the longest old suffix to the new prefix in linear row comparisons.
            // Repeated blank/progress rows otherwise retry nearly every candidate shift.
            if (_overlapPrefix.Length < beforeRows) _overlapPrefix = new int[beforeRows];
            _overlapPrefix[0] = 0;
            int matched = 0;
            for (int row = 1; row < beforeRows; row++)
            {
                while (matched > 0 && after[row] != after[matched])
                    matched = _overlapPrefix[matched - 1];
                if (after[row] == after[matched]) matched++;
                _overlapPrefix[row] = matched;
            }
            matched = 0;
            // Skip the old first row: a zero shift is not a scroll.
            for (int row = 1; row < beforeRows; row++)
            {
                while (matched > 0 && before[row] != after[matched])
                    matched = _overlapPrefix[matched - 1];
                if (before[row] == after[matched]) matched++;
            }
            return matched == 0 ? 0 : beforeRows - matched;
        }
    }
}
