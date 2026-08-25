using System.Collections.Generic;

namespace SlopWorld
{
    public partial class ScreenBuf
    {
        public int Seq = -1;
        public int Cols, Rows, Cx, Cy;
        // Lines scrolled up into scrollback; 0 for a live bottom frame.
        public int Off;
        // Rows the live frame moved upward since the previous live frame. Scrollback frames
        // leave this at zero; the terminal uses it to keep a selection attached to output that
        // just scrolled off the bottom.
        public int LiveShift;
        // Echoed from the scroll request this frame answers; 0 for a live frame. The terminal
        // accepts only the response to its latest request, so a stale reply cannot clamp it.
        public ulong ScrollRequestId;
        // 0 = block, 1 = underline, 2 = beam.
        public int CursorShape;
        public bool CursorBlink = true;
        public bool AppMouse;
        // Without this a drag is ours, and selecting text in a pane needs no Shift.
        public bool AppDrag;
        // The app is on the alternate screen (no scrollback of its own).
        public bool AltScreen;
        // What the app calls itself (OSC 0/2); empty until it says.
        public string Title = "";
        public string[] Lines = new string[0];

        // Parsed lazily by the terminal window and thrown away when Seq moves.
        public List<SgrRun>[] Runs;
        // Which palette the runs were parsed against; a scheme change re-parses them.
        public int RunsRev = -1;
    }
}
