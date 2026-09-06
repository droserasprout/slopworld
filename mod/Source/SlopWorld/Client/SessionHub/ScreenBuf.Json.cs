using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public partial class ScreenBuf
    {
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
            var nextLines = s["lines"].Items.Select(l => l.AsString()).ToArray();
            bool sameShape = previousLines != null && previousLines.Length == nextLines.Length &&
                previousCols == Cols && previousRows == Rows;
            var changed = new List<int>();
            if (sameShape)
            {
                for (int i = 0; i < nextLines.Length; i++)
                    if (!string.Equals(previousLines[i], nextLines[i],
                                       System.StringComparison.Ordinal))
                        changed.Add(i);
            }
            else
            {
                for (int i = 0; i < nextLines.Length; i++) changed.Add(i);
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
                var retained = new List<SgrRun>[nextLines.Length];
                for (int i = 0; i < nextLines.Length; i++)
                    if (string.Equals(previousLines[i], nextLines[i],
                                      System.StringComparison.Ordinal))
                        retained[i] = previousRuns[i];
                Runs = retained;
                RunsRev = previousRunsRev;
                RunsComplete = previousRunsComplete && changed.Count == 0;
            }
            else
            {
                Runs = null;
                RunsRev = -1;
                RunsComplete = false;
            }

            // A known URL screen is reparsed globally when its text changes so links spanning
            // physical rows remain correct. A screen previously known to contain no URL only
            // needs candidate checks in rows that changed.
            HasLinks = previousHasLinks;
            LinksKnown = !contentChanged && previousSeq >= 0;
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
            LiveShift = System.Math.Max(visibleShift, historyShift);
        }

        static int VerticalShift(string[] before, string[] after,
            int beforeRows, int afterRows, int beforeCy)
        {
            if (before == null || after == null || beforeRows < 2 || beforeRows != afterRows ||
                before.Length != after.Length || beforeCy < beforeRows - 1)
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

            int max = beforeRows - 1;
            for (int shift = 1; shift <= max; shift++)
            {
                bool overlap = true;
                for (int row = 0; row < beforeRows - shift; row++)
                {
                    if (before[row + shift] != after[row])
                    {
                        overlap = false;
                        break;
                    }
                }
                // An unchanged top row is not enough to reject a scroll: the row that
                // leaves the viewport may be repeated, while the rows below it still prove
                // that the screen moved. Keep the old ambiguity guard when the only change is
                // the bottom row, where a normal in-place edit is indistinguishable from a
                // scroll through identical content.
                if (!overlap || !changedBeforeBottom ||
                    before[beforeRows - 1] == after[afterRows - 1])
                    continue;
                return shift;
            }
            return 0;
        }
    }
}
