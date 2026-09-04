using System.Text;

namespace SlopWorld
{
    // Per-window state for the session binding and terminal input. Runtime coordination lives
    // in TerminalWindow.Runtime.cs; history, selection, and rendering state remain owned by
    // their respective concerns as those collaborators are extracted.
    sealed class TerminalWindowState
    {
        public string Name;
        public bool ShowStopped;
        public IContentView Content;
        public readonly StringBuilder Literal = new StringBuilder();
        public int SemicolonFrame = -1;
        public int Cols, Rows;
        public float ResizeAt;
        public bool SizeDirty;
        public float CursorBlinkAt;
        public int DroppedKeys;
        public readonly TerminalSelectionState Selection = new TerminalSelectionState();

        public static int CachedCols, CachedRows;
    }
}
