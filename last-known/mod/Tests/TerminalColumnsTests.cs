using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class TerminalColumnsTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("plain ascii maps column to string index", PlainAscii);
            yield return ("wide char does not drift the columns after it", WideCharNoDrift);
            yield return ("a reserved column resolves to its wide glyph", ReservedColumnResolves);
            yield return ("copying a range drops reserved columns", SliceDropsSpacers);
            yield return ("adjacent wide chars copy as themselves", AdjacentWideChars);
            yield return ("trailing blanks are not content", TrailingBlanks);
            yield return ("Line is column-indexed with spacers as blanks", LineIsColumnIndexed);
        }

        // A run of runs, the way ScreenBuf holds a parsed row. Set only what TerminalColumns
        // reads: the absolute column and the text.
        static List<SgrRun> Row(params (int Col, string Text)[] runs)
        {
            var list = new List<SgrRun>();
            foreach (var run in runs) list.Add(new SgrRun { Col = run.Col, Text = run.Text });
            return list;
        }

        static void PlainAscii()
        {
            var cells = TerminalColumns.Cells(Row((0, "hello")));
            AssertEx.Equal(5, TerminalColumns.ContentColumns(cells), "five columns of content");
            AssertEx.Equal('h', TerminalColumns.Glyph(cells, 0), "column 0 is h");
            AssertEx.Equal('o', TerminalColumns.Glyph(cells, 4), "column 4 is o");
            AssertEx.Equal("hello", TerminalColumns.Slice(cells, 0, 4), "the whole line");
            AssertEx.Equal("ell", TerminalColumns.Slice(cells, 1, 3), "an inner range");
        }

        // The bug: a wide glyph reserves two columns, so the daemon re-anchors the following
        // run at column 2 while the glyph is one string char. A packed concatenation would put
        // 'a' at index 1; the real column of 'a' is 2.
        static void WideCharNoDrift()
        {
            var cells = TerminalColumns.Cells(Row((0, "好"), (2, "abc")));
            AssertEx.Equal(5, TerminalColumns.ContentColumns(cells), "好 + abc spans five columns");
            AssertEx.Equal('好', TerminalColumns.Glyph(cells, 0), "the wide glyph sits at column 0");
            AssertEx.Equal('a', TerminalColumns.Glyph(cells, 2), "'a' is at column 2, not column 1");
            AssertEx.Equal('b', TerminalColumns.Glyph(cells, 3), "'b' is at column 3");
            AssertEx.Equal('c', TerminalColumns.Glyph(cells, 4), "'c' is at column 4");
            // Selecting the tail by column now lands on the right characters.
            AssertEx.Equal("abc", TerminalColumns.Slice(cells, 2, 4), "the tail copies as abc");
            AssertEx.Equal("好abc", TerminalColumns.Slice(cells, 0, 4), "the whole line copies once");
        }

        static void ReservedColumnResolves()
        {
            var cells = TerminalColumns.Cells(Row((0, "好"), (2, "abc")));
            // Column 1 is the wide glyph's reserved half; a click there selects the glyph.
            AssertEx.Equal('好', TerminalColumns.Glyph(cells, 1), "the reserved column reports 好");
        }

        static void SliceDropsSpacers()
        {
            var cells = TerminalColumns.Cells(Row((0, "好"), (2, "abc")));
            // A range that spans the glyph and its reserved column copies the glyph once, not
            // a glyph followed by a blank.
            AssertEx.Equal("好", TerminalColumns.Slice(cells, 0, 1), "the glyph copies once");
            // A range that opens on the reserved column has left the glyph behind.
            AssertEx.Equal("ab", TerminalColumns.Slice(cells, 1, 3), "starting past the glyph drops it");
        }

        static void AdjacentWideChars()
        {
            // 你好: each glyph reserves two columns and re-anchors the next run, but the daemon
            // never emits a trailing spacer, so the final glyph's reserved column is absent -
            // exactly like serialize_row trimming the last spacer cell.
            var cells = TerminalColumns.Cells(Row((0, "你"), (2, "好")));
            AssertEx.Equal(3, cells.Length, "the trailing reserved column is not emitted");
            AssertEx.Equal("你好", TerminalColumns.Slice(cells, 0, 2), "both copy without blanks between");
        }

        static void TrailingBlanks()
        {
            var cells = TerminalColumns.Cells(Row((0, "hi   ")));
            AssertEx.Equal(2, TerminalColumns.ContentColumns(cells), "trailing spaces are not content");
            AssertEx.Equal(5, cells.Length, "but the cells still cover every drawn column");
        }

        static void LineIsColumnIndexed()
        {
            var line = TerminalColumns.Line(TerminalColumns.Cells(Row((0, "好"), (2, "ab"))));
            AssertEx.Equal(4, line.Length, "one column per cell");
            AssertEx.Equal('好', line[0], "glyph at its column");
            AssertEx.Equal(' ', line[1], "reserved column reads as a blank");
            AssertEx.Equal('a', line[2], "and the tail keeps its columns");
        }
    }
}
