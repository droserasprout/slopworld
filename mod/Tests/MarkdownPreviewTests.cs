using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class MarkdownPreviewTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("copy separates blocks", CopySeparatesBlocks);
            yield return ("copy preserves wrapped prose spaces", CopyPreservesSpaces);
            yield return ("copy follows complete table cells", CopyTableCells);
            yield return ("code copy preserves whitespace at every width", CopyCode);
            yield return ("wrapped code marks continuation lines", CodeContinuations);
            yield return ("inline code reserves chip padding", InlineCodePadding);
            yield return ("typography invalidation rebuilds styles", TypographyInvalidation);
            yield return ("ordered markers reserve a bounded gutter", OrderedMarkerGutter);
            yield return ("tight and loose lists remain distinct", ListTightness);
            yield return ("headings preserve all six levels", SixHeadingLevels);
            yield return ("tables preserve alignment and content widths", TableGeometry);
            yield return ("nested list geometry does not overlap", NestedListGeometry);
            yield return ("heading code keeps monospace at heading size", HeadingCodeFont);
            yield return ("mixed font ascents and image bounds align", MixedBaselines);
            yield return ("terminal font revision reflows block geometry", TerminalTypography);
            yield return ("narrow table selection stays inside cells", NarrowTableBounds);
            yield return ("ordinary and reference images keep alt text", ImagesKeepAltText);
            yield return ("linked images keep their enclosing link", LinkedImagesKeepLinks);
            yield return ("rejected image schemes never resolve locally", RejectsImageSchemes);
            yield return ("relative URI paths decode after suffix removal", DecodesUriPaths);
            yield return ("encoded traversal remains outside the project", RejectsEncodedTraversal);
        }

        static TextLayout Wrap(string text, float width, bool code = false) =>
            new MarkdownTextLayout(new StyleSet(), (run, available) => new ImageMetrics(1, 1))
                .Wrap(new List<InlineRun> { new InlineRun { Text = text, Code = code } }, width, 0);

        static string Copy(List<SelectionLine> lines) => MarkdownSelectionText.CopyRange(lines,
            new Vector2Int(0, 0), new Vector2Int(lines[lines.Count - 1].Text.Length, lines.Count - 1));

        static void CopySeparatesBlocks()
        {
            var lines = new List<SelectionLine>();
            MarkdownSelectionText.CollectText(lines, Wrap("Hello", 100), 0, 0);
            MarkdownSelectionText.CollectText(lines, Wrap("World", 100), 0, 20);
            AssertEx.Equal("Hello\nWorld", Copy(lines), "blocks retain a separator");
        }

        static void CopyPreservesSpaces()
        {
            foreach (float width in new[] { 3f, 5f, 6f, 100f })
            {
                var lines = new List<SelectionLine>();
                MarkdownSelectionText.CollectText(lines, Wrap("hello world", width), 0, 0);
                AssertEx.Equal("hello world", Copy(lines), "spaces survive wrapping at " + width);
            }
        }

        static void CopyTableCells()
        {
            var table = new TableLayout { Widths = new[] { 5f, 5f } };
            var row = new TableRowLayout();
            row.Cells.Add(Wrap("hello world", 40));
            row.Cells.Add(Wrap("other cell", 40));
            row.Cells[0].Lines[1].Offset = 80;
            table.Rows.Add(row);
            var lines = new List<SelectionLine>();
            MarkdownSelectionText.CollectTable(lines, new Placement { Table = table });
            AssertEx.Equal("hello world\nother cell", Copy(lines), "whole cells copy in column order");
            AssertEx.True(lines[1].Y > lines[2].Y, "logical order is independent of line height");
        }

        static void CopyCode()
        {
            const string source = "    if ok:\n\twork()\n\n    done()\n";
            foreach (float width in new[] { 1f, 5f, 100f })
            {
                var lines = new List<SelectionLine>();
                MarkdownSelectionText.CollectText(lines, Wrap(source, width, true), 0, 0);
                AssertEx.Equal(source, Copy(lines), "code whitespace survives width " + width);
            }
        }

        static void CodeContinuations()
        {
            var layout = Wrap("abcdef", 3f, true);
            AssertEx.True(layout.Lines.Count >= 2, "narrow code wraps into visual lines");
            AssertEx.True(layout.Lines[1].Continuation,
                "soft-wrapped code line is visually marked as a continuation");
            AssertEx.Equal("abcdef", CopyCodeLines(layout),
                "continuation treatment does not change copied source");
        }

        static string CopyCodeLines(TextLayout layout)
        {
            var lines = new List<SelectionLine>();
            MarkdownSelectionText.CollectText(lines, layout, 0, 0);
            return Copy(lines);
        }

        static void InlineCodePadding()
        {
            var run = new InlineRun { Text = "x", Code = true, InlineCode = true };
            var layout = new MarkdownTextLayout(new StyleSet(), (item, available) =>
                new ImageMetrics(1, 1)).Wrap(new List<InlineRun> { run }, 100, 0);
            var piece = layout.Lines[0].Pieces[0];
            AssertEx.Equal(UiTheme.GapXS, piece.PaddingLeft, "inline chip has left padding");
            AssertEx.Equal(UiTheme.GapXS, piece.PaddingRight, "inline chip has right padding");
            AssertEx.Equal(8f + UiTheme.GapXS * 2f, piece.Width,
                "inline chip width includes both paddings");
        }

        static void TypographyInvalidation()
        {
            var styles = new StyleSet();
            var oldNormal = styles.Normal;
            var layout = new MarkdownTextLayout(styles, (run, available) =>
                new ImageMetrics(1, 1));
            layout.InvalidateMetrics();
            AssertEx.True(!ReferenceEquals(oldNormal, styles.Normal),
                "metrics invalidation replaces cached styles");
        }

        static void OrderedMarkerGutter()
        {
            AssertEx.Equal(32f, MarkdownTableGeometry.MarkerGutter(100, 24),
                "wide ordered marker gets marker width plus spacing");
            AssertEx.Equal(20f, MarkdownTableGeometry.MarkerGutter(20, 24),
                "marker gutter never exceeds the available list width");
        }

        static void ListTightness()
        {
            var tight = Parser().Parse("- one\n- two")[0];
            var loose = Parser().Parse("- one\n\n- two")[0];
            AssertEx.True(tight.Kind == BlockKind.List && tight.Tight,
                "ordinary list is tight");
            AssertEx.True(loose.Kind == BlockKind.List && !loose.Tight,
                "blank item separation preserves loose list semantics");
        }

        static void SixHeadingLevels()
        {
            var blocks = Parser().Parse("# one\n\n## two\n\n### three\n\n#### four\n\n##### five\n\n###### six");
            AssertEx.Equal(6, blocks.Count, "all heading blocks are retained");
            for (int i = 0; i < blocks.Count; i++)
                AssertEx.Equal(i + 1, blocks[i].Level, "heading level " + (i + 1));
        }

        static void TableGeometry()
        {
            var widths = MarkdownTableGeometry.AllocateColumns(100,
                new[] { 20f, 20f }, new[] { 20f, 100f });
            AssertEx.Equal(20f, widths[0], "short numeric column stays near its need");
            AssertEx.Equal(80f, widths[1], "prose column receives remaining viewport width");
            AssertEx.Equal(100f, widths[0] + widths[1], "column widths fill the viewport");

            var table = Parser().Parse("| n | prose |\n| ---: | :---: |\n| 1 | text |")[0];
            AssertEx.Equal(TableAlignment.Right, table.ColumnAlignments[0],
                "right table alignment is retained");
            AssertEx.Equal(TableAlignment.Center, table.ColumnAlignments[1],
                "center table alignment is retained");
            AssertEx.Equal(0f, MarkdownTableGeometry.Padding(1f),
                "narrow table cells remove padding before overflowing");
        }

        static MarkdownLayoutEngine Flow(string source, float width = 300f)
        {
            var engine = new MarkdownLayoutEngine(new MarkdownResourceStore(new MarkdownPathResolver("", "")));
            engine.Reflow(Parser().Parse(source), width);
            return engine;
        }

        static void NestedListGeometry()
        {
            var engine = Flow("100. outer\n     - nested\n101. next");
            float lastBottom = 0f;
            float firstX = -1f;
            foreach (var placement in engine.Placements)
            {
                if (placement.Kind != PlacementKind.Text) continue;
                AssertEx.True(placement.Y >= lastBottom, "nested text never overlaps a sibling");
                lastBottom = placement.Y + placement.Height;
                if (firstX < 0f) firstX = placement.X;
            }
            foreach (var placement in engine.Placements)
                if (placement.Kind == PlacementKind.Bullet && placement.X == UiTheme.GapM)
                    AssertEx.True(placement.X + placement.Text.Width < firstX,
                        "three-digit marker leaves a gap before content");
            var tight = Flow("- one\n- two");
            var loose = Flow("- one\n\n- two");
            AssertEx.True(loose.Height > tight.Height, "loose list changes actual block spacing");
        }

        static void HeadingCodeFont()
        {
            var styles = new StyleSet();
            for (int heading = 1; heading <= 6; heading++)
            {
                var prose = styles.For(new InlineRun(), heading);
                var code = styles.For(new InlineRun { Code = true }, heading);
                AssertEx.True(ReferenceEquals(code.font, TerminalFont.Style.font),
                    "heading code retains terminal face");
                int size = prose.fontSize > 0 ? prose.fontSize : prose.font.fontSize;
                AssertEx.Equal(size, code.fontSize, "code follows heading size even for baked fonts");
                AssertEx.Equal(styles.MeasureChar(code, 'W'), styles.MeasureChar(code, 'i'),
                    "code glyph advances stay monospaced");
            }
            AssertEx.True(styles.H1.fontSize > styles.H2.fontSize &&
                styles.H2.fontSize > styles.H3.font.fontSize,
                "zero-size baked styles still get distinct heading sizes");
        }

        static void MixedBaselines()
        {
            var styles = new StyleSet();
            var layout = new MarkdownTextLayout(styles, (run, width) => new ImageMetrics(20, 40))
                .Wrap(new List<InlineRun>
                {
                    new InlineRun { Text = "Wi" },
                    new InlineRun { Text = "code", Code = true, InlineCode = true },
                    new InlineRun { IsImage = true },
                }, 300, 0);
            var line = layout.Lines[0];
            AssertEx.True(styles.MeasureChar(styles.Normal, 'W') > styles.MeasureChar(styles.Normal, 'i'),
                "test exercises unequal proportional advances");
            var prose = line.Pieces[0];
            var code = line.Pieces[1];
            AssertEx.Equal(prose.OffsetY + StyleSet.Baseline(prose.Style),
                code.OffsetY + StyleSet.Baseline(code.Style), "font ascents share a baseline");
            foreach (var piece in line.Pieces)
                AssertEx.True(piece.OffsetY + piece.Height <= line.Height,
                    "line bounds contain shifted images and font descenders");
            AssertEx.True(line.Height > 40f, "descenders extend below the inline image baseline");
        }

        static void TerminalTypography()
        {
            var oldStyle = TerminalFont.Style;
            int oldRevision = TerminalFont.Rev;
            try
            {
                var blocks = Parser().Parse("```\nWWWW\n```");
                var engine = new MarkdownLayoutEngine(new MarkdownResourceStore(new MarkdownPathResolver("", "")));
                engine.Reflow(blocks, 300);
                var before = engine.Placements[0];
                TerminalFont.Style = new GUIStyle(oldStyle) { fontSize = 24 };
                TerminalFont.Rev++;
                engine.Reflow(blocks, 300);
                AssertEx.True(engine.Placements[0].Text.Width > before.Text.Width,
                    "same-width reflow sees a terminal font change without UI metric changes");
                var settled = engine.Placements[0];
                engine.Reflow(blocks, 300);
                AssertEx.True(ReferenceEquals(settled, engine.Placements[0]),
                    "unchanged frames retain the settled geometry");
            }
            finally { TerminalFont.Style = oldStyle; TerminalFont.Rev = oldRevision; }
        }

        static void NarrowTableBounds()
        {
            var engine = Flow("| a | b |\n| ---: | :---: |\n| `W` | W |", 40);
            var placement = engine.Placements[0];
            var lines = new List<SelectionLine>();
            MarkdownSelectionText.CollectTable(lines, placement);
            foreach (var line in lines)
                AssertEx.True(line.X + line.Width <= placement.X + placement.Width,
                    "selection is clipped to the table even when one glyph cannot fit");
            AssertEx.Equal("a\nb\nW\nW", Copy(lines),
                "clipping preserves complete source text for copying");
            foreach (var row in placement.Table.Rows)
                foreach (var cell in row.Cells)
                    AssertEx.True(cell.Lines.Count > 0, "narrow cells retain source lines");
        }

        static MarkdownDocumentParser Parser()
        {
            var paths = new MarkdownPathResolver("demo", "/work/demo/docs/readme.md",
                () => "/work/demo");
            return new MarkdownDocumentParser(paths);
        }

        static void ImagesKeepAltText()
        {
            var blocks = Parser().Parse("![diagram](diagram.png)\n\n![][missing]\n\n[missing]: empty.png");
            var first = blocks[0].Runs[0];
            var second = blocks[1].Runs[0];

            AssertEx.True(first.IsImage, "ordinary markdown image is an image run");
            AssertEx.Equal("diagram", first.ImageAlt, "ordinary image keeps alt text");
            AssertEx.Equal("diagram.png", first.ImagePath, "ordinary image keeps source path");
            AssertEx.True(second.IsImage, "reference markdown image is an image run");
            AssertEx.Equal("", second.ImageAlt, "empty alt text is retained exactly");
            AssertEx.Equal("empty.png", second.ImagePath, "reference image resolves its definition");
        }

        static void LinkedImagesKeepLinks()
        {
            var blocks = Parser().Parse("[![diagram](diagram.png)](other.md)");
            var image = blocks[0].Runs[0];

            AssertEx.True(image.IsImage, "linked image remains an image run");
            AssertEx.Equal("diagram", image.ImageAlt, "linked image keeps alt text");
            AssertEx.Equal("/work/demo/docs/other.md", image.LocalLink,
                "enclosing relative link is retained");
            AssertEx.Equal(null, image.Link, "local image link has no external URL");
        }

        static void RejectsImageSchemes()
        {
            var resolver = new MarkdownPathResolver("demo", "/work/demo/docs/readme.md",
                () => "/work/demo");
            AssertEx.Equal(null, resolver.ResolveImagePath("https://example.test/a.png"),
                "remote images stay disabled");
            AssertEx.Equal(null, resolver.ResolveImagePath("data:image/png;base64,AA=="),
                "data images stay disabled");
            AssertEx.Equal(null, resolver.ResolveImagePath("ftp://example.test/a.png"),
                "unsupported image schemes stay disabled");
        }

        static void DecodesUriPaths()
        {
            var resolver = new MarkdownPathResolver("demo", "/work/demo/docs/readme.md",
                () => "/work/demo");
            AssertEx.Equal("/work/demo/docs/my file.md",
                resolver.TryLocal("my%20file.md?view=raw#section"),
                "query and fragment are removed before percent decoding");
            AssertEx.Equal("/work/demo/docs/café.md",
                resolver.TryLocal("caf%C3%A9.md"), "Unicode URI paths decode");
            AssertEx.Equal("/work/demo/docs/100%done.md",
                resolver.TryLocal("100%25done.md"), "encoded literal percent decodes once");
        }

        static void RejectsEncodedTraversal()
        {
            var resolver = new MarkdownPathResolver("demo", "/work/demo/docs/readme.md",
                () => "/work/demo");
            AssertEx.Equal(null, resolver.TryLocal("%2e%2e/%2e%2e/etc/passwd"),
                "encoded traversal is checked after decoding");
        }
    }

    static class MarkdownPathResolverTestExtensions
    {
        public static string TryLocal(this MarkdownPathResolver resolver, string source)
        {
            resolver.TryResolveLink(source, out var external, out var local);
            return external == null ? local : external;
        }
    }
}
