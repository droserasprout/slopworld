using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class MarkdownSelectionTests
    {
        public static void CopySeparatesBlocks()
        {
            var lines = new List<SelectionLine>();
            MarkdownSelectionText.CollectText(lines, PreviewWrap("Hello", 100), 0, 0);
            MarkdownSelectionText.CollectText(lines, PreviewWrap("World", 100), 0, 20);
            AssertEx.Equal("Hello\nWorld", PreviewCopy(lines), "blocks retain a separator");
        }
        public static void CopyPreservesSpaces()
        {
            foreach (float width in new[] { 3f, 5f, 6f, 100f })
            {
                var lines = new List<SelectionLine>();
                MarkdownSelectionText.CollectText(lines, PreviewWrap("hello world", width), 0, 0);
                AssertEx.Equal("hello world", PreviewCopy(lines), "spaces survive wrapping at " + width);
            }
        }

        public static void CopyTableCells()
        {
            var table = new TableLayout { Widths = new[] { 5f, 5f } };
            var row = new TableRowLayout();
            row.Cells.Add(PreviewWrap("hello world", 40));
            row.Cells.Add(PreviewWrap("other cell", 40));
            row.Cells[0].Lines[1].Offset = 80;
            table.Rows.Add(row);
            var lines = new List<SelectionLine>();
            MarkdownSelectionText.CollectTable(lines, new Placement { Table = table });
            AssertEx.Equal("hello world\nother cell", PreviewCopy(lines), "whole cells copy in column order");
            AssertEx.True(lines[1].Y > lines[2].Y, "logical order is independent of line height");
        }

        public static void CopyCode()
        {
            const string source = "    if ok:\n\twork()\n\n    done()\n";
            foreach (float width in new[] { 1f, 5f, 100f })
            {
                var lines = new List<SelectionLine>();
                MarkdownSelectionText.CollectText(lines, PreviewWrap(source, width, true), 0, 0);
                AssertEx.Equal(source, PreviewCopy(lines), "code whitespace survives width " + width);
            }
        }

        static TextLayout PreviewWrap(string text, float width, bool code = false) =>
            new MarkdownTextLayout(new StyleSet(), (run, available) => new ImageMetrics(1, 1))
                .Wrap(new List<InlineRun> { new InlineRun { Text = text, Code = code } }, width, 0);

        static string PreviewCopy(List<SelectionLine> lines) => MarkdownSelectionText.CopyRange(lines,
            new Vector2Int(0, 0), new Vector2Int(lines[lines.Count - 1].Text.Length, lines.Count - 1)) + (lines[lines.Count - 1].Source.CopySuffix ?? "");

    }
}
