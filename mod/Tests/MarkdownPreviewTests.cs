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
            row.Cells.Add(Wrap("hello world", 5));
            row.Cells.Add(Wrap("other cell", 5));
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
