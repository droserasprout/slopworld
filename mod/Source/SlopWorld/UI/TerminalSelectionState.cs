using UnityEngine;

namespace SlopWorld
{
    // Selection state is kept separately from TerminalWindow's session and render state. The
    // gesture controller and renderer access it through the small properties on the window.
    sealed class TerminalSelectionState
    {
        public bool Dragging;
        public bool SelectionMoved;
        public bool MultiClickSelection;
        public bool WordDragging;
        public bool LineDragging;
        public Vector2Int WordStart, WordEnd;
        public int LineStart;
        public int Control;
        public bool HasSelection;
        public Vector2Int A, B;
        public Vector2 Mouse;
        public int EdgeDirection;
        public int EdgeFrame = -1;
    }
}
