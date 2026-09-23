using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class TerminalColumnsTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("scanner preserves columns within its buffer slice", ScanLineGeometry);
            yield return ("scanner reuses storage without allocating", ScanLineAllocations);
            yield return ("daemon markers own geometry", DaemonGeometry);
            yield return ("supplementary glyphs retain complete copy text", SupplementaryCopy);
            yield return ("selection copies either half of a trailing glyph", SelectionCopy);
            yield return ("links preserve widths and following columns", LinkGeometry);
            yield return ("plain ascii maps column to string index", PlainAscii);
            yield return ("wide char does not drift the columns after it", WideCharNoDrift);
            yield return ("wide char keeps both cells at the end", WideCharAtEnd);
            yield return ("a reserved column resolves to its wide glyph", ReservedColumnResolves);
            yield return ("highlight range expands around a wide char", WideRangeExpands);
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

        static void ScanLineGeometry()
        {
            var runs = Row((-1, "ab"), (2, "😀x"), (5, "q"), (7, "clipped"));
            runs.Add(new SgrRun { Col = 4, Text = "好", CellWidth = 2 });
            var buffer = "!!!!!!!!!!".ToCharArray();
            TerminalColumns.WriteScanLine(runs, buffer, 1, 8);
            AssertEx.Equal("!b \ufffdx好  c!", new string(buffer), "scalar, gap, wide continuation and clipping");
            TerminalColumns.WriteScanLine(Row((0, "z")), buffer, 1, 8);
            AssertEx.Equal("!z       !", new string(buffer), "reuse clears stale characters without touching neighbors");
        }

        static void ScanLineAllocations()
        {
            var runs = Row((0, new string('x', 120)), (120, "😀"));
            var buffer = new char[122];
            TerminalColumns.WriteScanLine(runs, buffer, 0, buffer.Length);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
                TerminalColumns.WriteScanLine(runs, buffer, 0, buffer.Length);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            AssertEx.Equal(0L, allocated, "screen scanning must not allocate per row or scalar");
        }

        static void DaemonGeometry()
        {
            // Deliberately use ASCII: geometry must follow the marker, not a width table.
            var runs = Sgr.ParseLine("\x1b[0;41mA\x1b[3G\x1b[32mB");
            AssertEx.Equal(2, runs[0].Columns, "background includes the reserved cell");
            AssertEx.Equal(2, runs[1].Col, "style change keeps the next column");
            AssertEx.True(runs[0].HasBg, "background survives the marker");
            var cells = TerminalColumns.Cells(runs);
            AssertEx.Equal("AB", TerminalColumns.Slice(cells, 0, 2), "continuation copies once");
            AssertEx.Equal(1, Sgr.ParseLine("好")[0].Columns, "unmarked text has no guessed width");
        }

        static void SupplementaryCopy()
        {
            foreach (string glyph in new[] { "\U00020000", "\U0001f600", "\U0001d400" })
            {
                var runs = Sgr.ParseLine("\x1b[0m" + glyph + "\x1b[3Gx");
                var cells = TerminalColumns.Cells(runs);
                AssertEx.Equal(2, runs[0].Columns, "wire width survives UTF-16");
                AssertEx.Equal(glyph, TerminalColumns.Slice(cells, 1, 1), "second half copies whole scalar");
                AssertEx.Equal(glyph + "x", TerminalColumns.Slice(cells, 0, 2), "following text is retained");
            }
            var narrow = TerminalColumns.Cells(Sgr.ParseLine("\U0001d400x"));
            AssertEx.Equal(2, narrow.Length, "a supplementary scalar need not be wide");
            AssertEx.Equal("\U0001d400x", TerminalColumns.Slice(narrow, 0, 1), "no surrogate is overwritten");
        }

        static void SelectionCopy()
        {
            var screen = new ScreenBuf { Lines = new[] { "\x1b[0m好\x1b[3G" } };
            var history = new TerminalHistory();
            AssertEx.Equal("好", history.SelectionText(screen, 1, 0, 1, 0), "copy second cell");
            AssertEx.Equal("好", history.SelectionText(screen, 0, 0, 0, 0), "copy first cell");
        }

        static void LinkGeometry()
        {
            var runs = Sgr.ParseLine("\U0001d400https://example.com 好\x1b[24G");
            AssertEx.Equal(3, runs[runs.Count - 1].Columns, "link slicing retains space and final wide glyph");
            AssertEx.Equal(1, runs[1].Col, "autolink starts after one scalar");
            AssertEx.Equal("https://example.com", runs[1].Url, "autolink survives supplementary prefix");
            var linked = Sgr.ParseLine("\x1b]8;;https://example.com\x1b\\好\x1b[3G\x1b]8;;\x1b\\");
            AssertEx.Equal(2, linked[0].Columns, "OSC closing does not lose the wide end");
            AssertEx.Equal("https://example.com", linked[0].Url, "explicit link retained");
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
        // 'a' at index 1. The real column of 'a' is 2.
        static void WideCharNoDrift()
        {
            var cells = TerminalColumns.Cells(Sgr.ParseLine("\x1b[0m好\x1b[3Gabc"));
            AssertEx.Equal(5, TerminalColumns.ContentColumns(cells), "好 + abc spans five columns");
            AssertEx.Equal('好', TerminalColumns.Glyph(cells, 0), "the wide glyph sits at column 0");
            AssertEx.Equal('a', TerminalColumns.Glyph(cells, 2), "'a' is at column 2, not column 1");
            AssertEx.Equal('b', TerminalColumns.Glyph(cells, 3), "'b' is at column 3");
            AssertEx.Equal('c', TerminalColumns.Glyph(cells, 4), "'c' is at column 4");
            // Selecting the tail by column now lands on the right characters.
            AssertEx.Equal("abc", TerminalColumns.Slice(cells, 2, 4), "the tail copies as abc");
            AssertEx.Equal("好abc", TerminalColumns.Slice(cells, 0, 4), "the whole line copies once");
        }

        static void WideCharAtEnd()
        {
            var cells = TerminalColumns.Cells(Sgr.ParseLine("\x1b[0m好\x1b[3G"));
            AssertEx.Equal(2, cells.Length, "the trailing spacer remains addressable");
            AssertEx.Equal('好', TerminalColumns.Glyph(cells, 1),
                           "the second cell still belongs to the glyph");
            AssertEx.Equal("好", TerminalColumns.Slice(cells, 0, 1),
                           "a trailing wide glyph copies once");
        }

        static void ReservedColumnResolves()
        {
            var cells = TerminalColumns.Cells(Sgr.ParseLine("\x1b[0m好\x1b[3Gabc"));
            // Column 1 is the wide glyph's reserved half. A click there selects the glyph.
            AssertEx.Equal('好', TerminalColumns.Glyph(cells, 1), "the reserved column reports 好");
        }

        static void WideRangeExpands()
        {
            var cells = TerminalColumns.Cells(Sgr.ParseLine("\x1b[0m好\x1b[3Gx"));
            int first = 0, end = 1;
            TerminalColumns.ExpandWideRange(cells, ref first, ref end);
            AssertEx.Equal(0, first, "selection keeps the glyph start");
            AssertEx.Equal(2, end, "selection covers the glyph's reserved cell");

            first = 1; end = 2;
            TerminalColumns.ExpandWideRange(cells, ref first, ref end);
            AssertEx.Equal(0, first, "selection from the reserved half moves to the glyph");
            AssertEx.Equal(2, end, "selection from the reserved half stays whole");
        }

        static void SliceDropsSpacers()
        {
            var cells = TerminalColumns.Cells(Sgr.ParseLine("\x1b[0m好\x1b[3Gabc"));
            // A range that spans the glyph and its reserved column copies the glyph once, not
            // a glyph followed by a blank.
            AssertEx.Equal("好", TerminalColumns.Slice(cells, 0, 1), "the glyph copies once");
            // A range opening on the continuation includes the highlighted glyph.
            AssertEx.Equal("好ab", TerminalColumns.Slice(cells, 1, 3), "copy agrees with highlight");
        }

        static void AdjacentWideChars()
        {
            // The final CHA preserves the last glyph's occupied end.
            var cells = TerminalColumns.Cells(Sgr.ParseLine("\x1b[0m你\x1b[3G好\x1b[5G"));
            AssertEx.Equal(4, cells.Length, "the final glyph keeps its reserved cell");
            AssertEx.Equal("你好", TerminalColumns.Slice(cells, 0, 3), "both copy without blanks between");
        }

        static void TrailingBlanks()
        {
            var cells = TerminalColumns.Cells(Row((0, "hi   ")));
            AssertEx.Equal(2, TerminalColumns.ContentColumns(cells), "trailing spaces are not content");
            AssertEx.Equal(5, cells.Length, "but the cells still cover every drawn column");
        }

        static void LineIsColumnIndexed()
        {
            var line = TerminalColumns.Line(TerminalColumns.Cells(Sgr.ParseLine("\x1b[0m好\x1b[3Gab")));
            AssertEx.Equal(4, line.Length, "one column per cell");
            AssertEx.Equal('好', line[0], "glyph at its column");
            AssertEx.Equal(' ', line[1], "reserved column reads as a blank");
            AssertEx.Equal('a', line[2], "and the tail keeps its columns");
        }
    }
}
