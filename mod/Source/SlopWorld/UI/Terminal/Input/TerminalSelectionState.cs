using UnityEngine;

namespace SlopWorld
{
    // Per-panel selection state, kept separate from the rest of TerminalPanelState. The
    // gesture controller gets a narrow panel surface. Panel partials use this state directly.
    sealed class TerminalSelectionState
    {
        public bool Dragging;
        public bool SelectionMoved;
        public bool MultiClickSelection;
        public bool WordDragging;
        public bool LineDragging;
        public Vector2Int WordStart,
            WordEnd;
        public int LineStart;
        public int Control;
        public bool HasSelection;
        public Vector2Int A,
            B;
        public Vector2 Mouse;
        public int EdgeDirection;
        public int EdgeFrame = -1;

        public int Offset { get; private set; }
        public ulong? LastLiveSeq { get; private set; }

        public void NoteLiveFrame(ulong liveSeq, int liveShift)
        {
            if (liveSeq == LastLiveSeq)
                return;
            // Every retained row moves relative to the live bottom, including rows in a
            // frozen history view. SyncOffset then compensates for that view's advancing
            // anchor, leaving its selection on the same text while output streams below.
            if (LastLiveSeq.HasValue && (HasSelection || Dragging || WordDragging || LineDragging))
                MoveRows(-liveShift);
            LastLiveSeq = liveSeq;
        }

        public void SyncOffset(int offset)
        {
            MoveRows(offset - Offset);
            Offset = offset;
        }

        public void ResetCoordinates()
        {
            Offset = 0;
            ResetLiveSequence();
        }

        public void ResetLiveSequence() => LastLiveSeq = null;

        void MoveRows(int delta)
        {
            if (delta == 0)
                return;
            A.y += delta;
            B.y += delta;
            WordStart.y += delta;
            WordEnd.y += delta;
            if (LineDragging)
                LineStart += delta;
        }
    }
}
