using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SlopWorld
{
    // Parsed rows are immutable. Weak keys keep geometry alive only as long as its row;
    // no frame/session retention and no per-scalar strings are needed to paint selection.
    internal sealed class TerminalSelectionGeometry
    {
        const byte Space = 0,
            Glyph = 1,
            Continuation = 2;
        readonly byte[] _cells;
        public readonly int ContentColumns;
        static readonly ConditionalWeakTable<List<SgrRun>, TerminalSelectionGeometry> Cache =
            new ConditionalWeakTable<List<SgrRun>, TerminalSelectionGeometry>();
        static readonly ConditionalWeakTable<
            List<SgrRun>,
            TerminalSelectionGeometry
        >.CreateValueCallback Create = runs => new TerminalSelectionGeometry(runs);

        public static TerminalSelectionGeometry For(List<SgrRun> runs) =>
            Cache.GetValue(runs, Create);

        TerminalSelectionGeometry(List<SgrRun> runs)
        {
            int width = 0;
            foreach (var run in runs)
                width = Math.Max(width, run.Col + run.Columns);
            _cells = new byte[width];
            foreach (var run in runs)
            {
                if (run.Text == null)
                    continue;
                int col = run.Col;
                if (run.IsCluster)
                {
                    if (col >= 0 && col < width)
                        _cells[col] = run.Text == " " ? Space : Glyph;
                    col++;
                }
                else
                    for (int i = 0; i < run.Text.Length; )
                    {
                        int units = TerminalColumns.ScalarUnits(run.Text, i);
                        if (col >= 0 && col < width)
                            _cells[col] = units == 1 && run.Text[i] == ' ' ? Space : Glyph;
                        col++;
                        i += units;
                    }
                for (; col < run.Col + run.Columns; col++)
                    if (col >= 0 && col < width)
                        _cells[col] = Continuation;
            }
            int content = width;
            while (content > 0 && _cells[content - 1] == Space)
                content--;
            ContentColumns = content;
        }

        public void ExpandWideRange(ref int first, ref int end)
        {
            if (_cells.Length == 0 || end <= first)
                return;
            first = Math.Max(0, Math.Min(first, _cells.Length));
            end = Math.Max(first, Math.Min(end, _cells.Length));
            while (first > 0 && first < _cells.Length && _cells[first] == Continuation)
                first--;
            while (end < _cells.Length && _cells[end] == Continuation)
                end++;
        }
    }
}
