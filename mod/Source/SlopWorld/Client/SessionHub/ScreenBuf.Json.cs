using System.Linq;

namespace SlopWorld
{
    public partial class ScreenBuf
    {
        public void FromJson(JVal s)
        {
            string[] previousLines = Lines;
            int previousRows = Rows;
            int previousCy = Cy;
            bool previousAltScreen = AltScreen;
            int previousSeq = Seq;

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
            Lines = s["lines"].Items.Select(l => l.AsString()).ToArray();
            LiveShift = Off == 0 && previousSeq >= 0 && !previousAltScreen && !AltScreen
                ? VerticalShift(previousLines, Lines, previousRows, Rows, previousCy)
                : 0;
            Runs = null; // force a re-parse on next draw
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
