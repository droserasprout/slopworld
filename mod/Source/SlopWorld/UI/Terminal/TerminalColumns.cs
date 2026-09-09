using System.Collections.Generic;
using System.Text;

namespace SlopWorld
{
    // Screen columns are not string offsets. The daemon reserves two columns for a wide
    // glyph and re-anchors the run after it with a CHA marker, so every run carries an
    // absolute Col and a plain concatenation of run text drifts left of the real columns
    // by one per wide glyph. The renderer and cursor honour Col (see Paint and
    // DrawCursorGlyph); selection, copy and path detection go through here so they measure
    // and slice in the same column space instead of a packed string.
    public static class TerminalColumns
    {
        // Column-indexed glyphs for one row. cells[c] is the character drawn at column c;
        // a wide glyph's reserved trailing column holds '\0'. A run's chars land at
        // run.Col, and a wide glyph is always the last char of its run - the daemon breaks
        // the run at the CHA that follows the spacer cell - so run.Col + k is the column of
        // every char in the run.
        public static char[] Cells(List<SgrRun> runs)
        {
            int width = 0;
            if (runs != null)
                foreach (var run in runs)
                {
                    int end = run.Col + (run.Text == null ? 0 : run.Text.Length);
                    if (end > width) width = end;
                }

            var cells = new char[width]; // default '\0' marks a reserved (spacer) column
            if (runs != null)
                foreach (var run in runs)
                {
                    string text = run.Text;
                    if (text == null) continue;
                    for (int k = 0; k < text.Length; k++)
                    {
                        int c = run.Col + k;
                        if (c >= 0 && c < width) cells[c] = text[k];
                    }
                }
            return cells;
        }

        // Columns of content, trailing blanks dropped. A wide glyph's reserved column
        // ('\0') is kept so the whole glyph stays selectable and its highlight covers it.
        public static int ContentColumns(char[] cells)
        {
            int n = cells.Length;
            while (n > 0 && cells[n - 1] == ' ') n--;
            return n;
        }

        // The glyph occupying a column. A wide glyph's reserved column reports the glyph
        // itself, so a click on either half resolves to the same character.
        public static char Glyph(char[] cells, int col)
        {
            if (col < 0 || col >= cells.Length) return '\0';
            char c = cells[col];
            while (c == '\0' && col > 0) c = cells[--col];
            return c;
        }

        // Text under an inclusive column range, reserved columns dropped so a wide glyph
        // copies once rather than as a glyph followed by a blank.
        public static string Slice(char[] cells, int firstColumn, int lastColumn)
        {
            if (firstColumn < 0) firstColumn = 0;
            if (lastColumn > cells.Length - 1) lastColumn = cells.Length - 1;
            var sb = new StringBuilder();
            for (int c = firstColumn; c <= lastColumn; c++)
                if (cells[c] != '\0') sb.Append(cells[c]);
            return sb.ToString();
        }

        // A flat line whose index is its column, reserved columns rendered as a space. For
        // scanners that want a string and a column to point into it at once.
        public static string Line(char[] cells)
        {
            var sb = new StringBuilder(cells.Length);
            foreach (char c in cells) sb.Append(c == '\0' ? ' ' : c);
            return sb.ToString();
        }
    }
}
