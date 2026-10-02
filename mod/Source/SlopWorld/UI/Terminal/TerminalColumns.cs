using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.CompilerServices;

namespace SlopWorld
{
    // Geometry comes from daemon CHA markers, not font metrics or Unicode width tables.
    // Plain runs end at a wide glyph. Cluster runs keep all their scalars in one
    // display unit and use the daemon's explicit occupied width.
    public static class TerminalColumns
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ScalarUnits(string text, int offset) =>
            char.IsHighSurrogate(text[offset]) && offset + 1 < text.Length &&
            char.IsLowSurrogate(text[offset + 1]) ? 2 : 1;

        public static int ScalarCount(string text)
        {
            int count = 0;
            if (text != null)
                for (int i = 0; i < text.Length; i += ScalarUnits(text, i)) count++;
            return count;
        }

        public static int TextOffset(string text, int column)
        {
            int i = 0;
            while (column-- > 0 && i < text.Length) i += ScalarUnits(text, i);
            return i;
        }

        // Complete scalar strings occupy leading cells. Null means a continuation.
        // unoccupied gaps are spaces, so selection cannot mistake them for wide glyphs.
        public static string[] Cells(List<SgrRun> runs)
        {
            int width = 0;
            if (runs != null)
                foreach (var run in runs) width = Math.Max(width, run.Col + run.Columns);
            var cells = new string[width];
            for (int c = 0; c < width; c++) cells[c] = " ";
            if (runs != null)
                foreach (var run in runs)
                {
                    if (run.Text == null) continue;
                    int col = run.Col;
                    if (run.IsCluster)
                    {
                        if (col >= 0 && col < width) cells[col] = run.Text;
                        for (int end = col + run.Columns; ++col < end;)
                            if (col >= 0 && col < width) cells[col] = null;
                        continue;
                    }
                    for (int i = 0; i < run.Text.Length;)
                    {
                        int units = ScalarUnits(run.Text, i);
                        if (col >= 0 && col < width) cells[col] = run.Text.Substring(i, units);
                        col++;
                        i += units;
                    }
                    for (; col < run.Col + run.Columns; col++)
                        if (col >= 0) cells[col] = null;
                }
            return cells;
        }

        // Link scans need one UTF-16 unit per column, not per-cell strings for copy/selection.
        // Write directly into reusable screen storage. Treat supplementary scalars as delimiters.
        // Keep daemon-reserved continuation cells blank after overlapping runs.
        internal static void WriteScanLine(List<SgrRun> runs, char[] buffer, int offset, int width)
        {
            for (int c = 0; c < width; c++) buffer[offset + c] = ' ';
            foreach (var run in runs)
            {
                if (run.Text == null) continue;
                int col = run.Col;
                if (run.IsCluster)
                {
                    if (col >= 0 && col < width) buffer[offset + col] = '\ufffd';
                    for (int end = col + run.Columns; ++col < end && col < width;)
                        if (col >= 0) buffer[offset + col] = ' ';
                    continue;
                }
                for (int i = 0; i < run.Text.Length && col < width;)
                {
                    char ch = run.Text[i++];
                    if (char.IsHighSurrogate(ch) && i < run.Text.Length && char.IsLowSurrogate(run.Text[i]))
                    {
                        ch = '\ufffd';
                        i++;
                    }
                    if (col >= 0) buffer[offset + col] = ch;
                    col++;
                }
                for (; col < run.Col + run.Columns && col < width; col++)
                    if (col >= 0) buffer[offset + col] = ' ';
            }
        }

        // Shared by paint and copy so either half selects the same complete glyph.
        public static void ExpandWideRange(string[] cells, ref int first, ref int end)
        {
            if (cells == null || cells.Length == 0 || end <= first) return;
            first = Math.Max(0, Math.Min(first, cells.Length));
            end = Math.Max(first, Math.Min(end, cells.Length));
            while (first > 0 && first < cells.Length && cells[first] == null) first--;
            while (end < cells.Length && cells[end] == null) end++;
        }

        public static int ContentColumns(string[] cells)
        {
            int n = cells.Length;
            while (n > 0 && cells[n - 1] == " ") n--;
            return n;
        }

        public static string GlyphText(string[] cells, int col)
        {
            if (col < 0 || col >= cells.Length) return "";
            while (col > 0 && cells[col] == null) col--;
            return cells[col] ?? "";
        }

        // Endpoints remain terminal columns; classify complete scalars and compare complete
        // non-word glyphs so adjacent emoji sharing a surrogate do not become one selection.
        internal static void WordRange(string[] cells, int column, out int first, out int last)
        {
            string anchor = GlyphText(cells, column);
            bool word = IsWordGlyph(anchor);
            first = last = column;
            int length = ContentColumns(cells);
            while (first > 0 && SameWordClass(GlyphText(cells, first - 1), anchor, word)) first--;
            while (last + 1 < length && SameWordClass(GlyphText(cells, last + 1), anchor, word)) last++;
        }

        static bool SameWordClass(string glyph, string anchor, bool word) =>
            word ? IsWordGlyph(glyph) : glyph == anchor;

        static bool IsWordGlyph(string glyph)
        {
            if (string.IsNullOrEmpty(glyph)) return false;
            if (char.IsLetterOrDigit(glyph, 0) || glyph == "_") return true;
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(glyph, 0);
            return category == System.Globalization.UnicodeCategory.NonSpacingMark ||
                category == System.Globalization.UnicodeCategory.SpacingCombiningMark;
        }

        // Legacy single-character access; selection and copy use complete glyph strings.
        public static char Glyph(string[] cells, int col)
        {
            string glyph = GlyphText(cells, col);
            return glyph.Length == 0 ? '\0' : glyph[0];
        }

        public static string Slice(string[] cells, int firstColumn, int lastColumn)
        {
            int end = Math.Min(cells.Length, lastColumn + 1);
            firstColumn = Math.Max(0, firstColumn);
            if (end <= firstColumn) return "";
            ExpandWideRange(cells, ref firstColumn, ref end);
            var sb = new StringBuilder();
            for (int c = firstColumn; c < end; c++) sb.Append(cells[c]);
            return sb.ToString();
        }

        // Scanners need one UTF-16 unit per column. Treat supplementary scalars as opaque delimiters.
        // Keep complete text in Cells for selection and copy.
        public static string Line(string[] cells)
        {
            var sb = new StringBuilder(cells.Length);
            foreach (string glyph in cells)
                sb.Append(glyph == null ? ' ' : glyph.Length == 1 ? glyph[0] : '\ufffd');
            return sb.ToString();
        }
    }
}
