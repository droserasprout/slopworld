using System.Text;

namespace SlopWorld
{
    // Per-panel state for the session binding and terminal input. The TerminalPanel partials
    // expose this state through concern-specific facades rather than owning it directly.
    sealed class TerminalPanelState
    {
        public string Name;
        public bool ShowStopped;
        public readonly StringBuilder Literal = new StringBuilder();
        public int SemicolonFrame = -1;
        public int Cols, Rows;
        public float ResizeAt;
        public bool SizeDirty;
        public float CursorBlinkAt;
        public int DroppedKeys;
        public readonly TerminalSelectionState Selection = new TerminalSelectionState();

    }
}
