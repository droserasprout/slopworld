using System.Text;

namespace SlopWorld
{
    // Session binding, buffered input, sizing, cursor timing, delivery counts and selection.
    // Panel partials access this state directly; input controllers use the panel facade.
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
