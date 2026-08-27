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

            // A real terminal scroll leaves the old tail at the new top. Require the rows
            // entering at the bottom and leaving at the top to be visibly different too;
            // otherwise an edit to a repeated line ("", for example) looks like a scroll.
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
                if (!overlap || before[0] == after[0] ||
                    before[beforeRows - 1] == after[afterRows - 1])
                    continue;
                return shift;
            }
            return 0;
        }
    }
}
