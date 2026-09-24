using System.Collections.Generic;

namespace SlopWorld
{
    public partial class ScreenBuf
    {
        static readonly int[] NoChangedRows = new int[0];

        public int Seq = -1;
        internal ulong WireSeq;
        internal Google.Protobuf.Collections.RepeatedField<Wire.InputTiming> InputTimings;
        public int Cols, Rows, Cx, Cy;
        // Change ContentRevision only when visible row text or terminal dimensions change.
        // Frames with only cursor changes can reuse the cached rendering surface.
        public int ContentRevision;
        public int[] ChangedRows = NoChangedRows;
        public bool HasLinks;
        public bool LinksKnown;
        // Scroll offset in history rows. Zero for a live frame at the bottom.
        public int Off;
        // Total available history rows.
        public int History;
        // Number of rows that live output moved upward since the previous frame.
        // History frames leave this at zero.
        // The terminal uses this value to keep selections attached to output as it scrolls into history.
        public int LiveShift;
        // ID of the scroll request that this frame answers. Zero for a live frame.
        // Accept only the latest request's response so stale replies cannot change the scroll position.
        public ulong ScrollRequestId;
        // 0 = block, 1 = underline, 2 = beam.
        public int CursorShape;
        public bool CursorBlink = true;
        public bool AppMouse;
        // When false, dragging selects terminal text without requiring Shift.
        public bool AppDrag;
        // The app is on the alternate screen (no scrollback of its own).
        public bool AltScreen;
        // Application title from OSC 0/2. Empty until the application supplies a title.
        public string Title = "";
        public string[] Lines = new string[0];

        // The terminal window parses runs on demand and invalidates them when Seq changes.
        public List<SgrRun>[] Runs;
        // Palette revision used to parse the runs. A scheme change requires parsing again.
        public int RunsRev = -1;
        public bool RunsComplete;
        // ANSI parsing is independent of screen-wide autolink decoration. Keeping the base
        // rows separate lets a sparse URL edit rebuild only the connected link span.
        public List<SgrRun>[] BaseRuns;
        public int BaseRunsRev = -1;

        // Immutable link spans from the last parse, shared by snapshots. Comparing the next
        // spans also invalidates unchanged rows of URLs edited while this tab was not drawn.
        internal List<UrlScan.Span>[] AutoLinks;

        // The history cache needs a stable frame while the live buffer continues receiving output.
        // Share parsed runs. FromWire replaces them only in the source buffer.
        // Copy incomplete arrays so parsing can fill null slots independently in the source and snapshot.
        public ScreenBuf Snapshot()
        {
            return new ScreenBuf
            {
                Seq = Seq,
                Cols = Cols,
                Rows = Rows,
                ContentRevision = ContentRevision,
                ChangedRows = ChangedRows,
                HasLinks = HasLinks,
                LinksKnown = LinksKnown,
                Cx = Cx,
                Cy = Cy,
                Off = Off,
                History = History,
                LiveShift = LiveShift,
                ScrollRequestId = ScrollRequestId,
                CursorShape = CursorShape,
                CursorBlink = CursorBlink,
                AppMouse = AppMouse,
                AppDrag = AppDrag,
                AltScreen = AltScreen,
                Title = Title,
                Lines = Lines == null ? new string[0] : (string[])Lines.Clone(),
                Runs = RunsComplete || Runs == null ? Runs : (List<SgrRun>[])Runs.Clone(),
                RunsRev = RunsRev,
                RunsComplete = RunsComplete,
                BaseRuns = BaseRuns,
                BaseRunsRev = BaseRunsRev,
                AutoLinks = AutoLinks,
            };
        }
    }
}
