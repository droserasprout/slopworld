using UnityEngine;

namespace SlopWorld
{
    public struct SgrRun
    {
        public string Text;
        // Absolute start column, advanced by scalars and the daemon's CHA markers.
        public int Col;
        // Plain runs use one cell per scalar, with a final wide glyph closed by CHA.
        // Clusters instead carry the daemon's occupied width for their whole text.
        public int CellWidth;
        public int Columns => CellWidth > 0 ? CellWidth : TerminalColumns.ScalarCount(Text);
        // A daemon cell (or matched sequence of cells) whose complete text occupies
        // CellWidth columns. Its internal scalars are not separate terminal cells.
        public bool IsCluster;
        public Color Fg;
        public Color Bg;
        public bool HasBg;
        public bool Bold;
        // The whole link this run is part of, whether the app said so (OSC 8) or the
        // text simply reads as a URL. Null for ordinary text.
        public string Url;
    }
}
