using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld.Tests
{
    static class MarkdownParsingTests
    {
        static List<MarkdownBlock> Parse(string source) => new MarkdownDocumentParser(
            new MarkdownPathResolver("demo", "/work/demo/docs/readme.md", () => "/work/demo")).Parse(source);

        static string Text(IEnumerable<InlineRun> runs) => string.Concat(runs.Select(run => run.Text));

        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            foreach (string tag in new[] { "b", "strong", "i", "em", "code", "kbd", "samp", "del", "s", "strike" })
                yield return ("HTML style scope: " + tag, () => HtmlStyleScope(tag));
            foreach (var dimension in new[] { ("32px", 32f), ("12.5", 12.5f), ("50%", 0f), ("-1", 0f), ("bad", 0f) })
                yield return ("HTML image dimension: " + dimension.Item1, () =>
                {
                    var image = Parse("before <img src='figure.png' width='" + dimension.Item1 + "' height='16PX'>")[0].Runs.Single(r => r.IsImage);
                    AssertEx.Equal(dimension.Item2, image.ImageWidth, "absolute positive dimensions only");
                    AssertEx.Equal(16f, image.ImageHeight, "pixel suffix is case insensitive");
                });
        }

        static void HtmlStyleScope(string tag)
        {
            var runs = Parse("before <" + tag + ">outer <" + tag + ">inner</" + tag + "> tail</" + tag + "> after")[0].Runs;
            foreach (var run in runs)
            {
                bool styled = run.Text != "before " && run.Text != " after";
                AssertEx.Equal(styled && (tag == "b" || tag == "strong"), run.Bold, "bold scope");
                AssertEx.Equal(styled && (tag == "i" || tag == "em"), run.Italic, "italic scope");
                bool code = styled && (tag == "code" || tag == "kbd" || tag == "samp");
                AssertEx.Equal(code, run.Code, "code scope");
                AssertEx.Equal(code, run.InlineCode, "inline code chip scope");
                AssertEx.Equal(styled && (tag == "del" || tag == "s" || tag == "strike"), run.Strike, "strike scope");
            }
            AssertEx.Equal("before outer inner tail after", Text(runs), "supported tags disappear without losing text");
        }

        public static void EntitiesDecodeOnceWithoutFallbackStyling()
        {
            var runs = Parse("&copy; &#169; &#x1F600; &amp;lt; **&trade;** `&amp;lt;`")[0].Runs;
            AssertEx.Equal("© © 😀 &lt; ™ &amp;lt;", Text(runs), "AST entities decode once while code stays literal");
            AssertEx.False(runs.Any(run => run.Faint), "entities are ordinary text");
            AssertEx.True(runs.Single(run => run.Text == "™").Bold, "entity keeps enclosing style");
            runs = Parse("<a href='https://example.test/&amp;lt;'>html</a> [md](https://example.test/&amp;lt;)")[0].Runs;
            AssertEx.Equal("https://example.test/&lt;", runs.Single(run => run.Text == "html").Link, "raw HTML URL decodes once");
            AssertEx.Equal("https://example.test/&lt;", runs.Single(run => run.Text == "md").Link, "Markdig URL is already decoded");
            var image = Parse("![&copy; &amp;lt;](a&amp;lt;.png)")[0].Runs.Single(run => run.IsImage);
            AssertEx.Equal("© &lt;", image.ImageAlt, "image alt uses entity AST nodes");
            AssertEx.Equal("a&lt;.png", image.ImagePath, "image path does not decode Markdig twice");
            image = Parse("before <img src='a&amp;lt;.png'>")[0].Runs.Single(run => run.IsImage);
            var resolver = new MarkdownPathResolver("demo", "/work/demo/readme.md", () => "/work/demo");
            AssertEx.Equal("/work/demo/a&lt;.png", resolver.ResolveImagePath(image.ImagePath), "resource resolution preserves normalized entities");
        }

        public static void MarkdownEmphasisLinksAndBreaksRetainMeaning()
        {
            var runs = Parse("**bold *both*** `literal` [local](next.md) [web](https://example.test) <https://example.test/auto>  \nnext")[0].Runs;
            AssertEx.True(runs.Single(r => r.Text == "bold ").Bold, "strong emphasis");
            AssertEx.True(runs.Single(r => r.Text == "both").Bold && runs.Single(r => r.Text == "both").Italic, "nested emphasis combines");
            AssertEx.True(runs.Single(r => r.Text == "literal").InlineCode, "code becomes a chip");
            AssertEx.Equal("/work/demo/docs/next.md", runs.Single(r => r.Text == "local").LocalLink, "local destination");
            AssertEx.Equal("https://example.test", runs.Single(r => r.Text == "web").Link, "external destination");
            AssertEx.Equal("https://example.test/auto", runs.Single(r => r.Text == "https://example.test/auto").Link, "autolink destination");
            AssertEx.True(Text(runs).EndsWith("\nnext", StringComparison.Ordinal), "explicit source break retained");
        }

        public static void HtmlLinksAndStructuralTagsDoNotLeakState()
        {
            var runs = Parse("start <a href='next.md'><b>local</b></a> plain <a href=\"https://example.test?a=1&amp;b=2\">web</a><br/>end <!-- hidden --> <span style='color:red'>span</span> <unknown>x</unknown>")[0].Runs;
            AssertEx.Equal("/work/demo/docs/next.md", runs.Single(r => r.Text == "local").LocalLink, "HTML local anchor");
            AssertEx.True(runs.Single(r => r.Text == "local").Bold, "nested HTML style");
            AssertEx.Equal(null, runs.Single(r => r.Text == " plain ").LocalLink, "closing anchor clears destination");
            AssertEx.Equal("https://example.test?a=1&b=2", runs.Single(r => r.Text == "web").Link, "attribute entities decode");
            AssertEx.False(Text(runs).Contains("hidden"), "inline comments are hidden");
            AssertEx.True(Text(runs).Contains("\nend"), "HTML break retained");
            AssertEx.False(runs.Single(r => r.Text == "span").Faint, "structural span leaves normal text");
            AssertEx.True(runs.Single(r => r.Text == "<unknown>").Faint, "unsupported tags remain visible as faint text");
        }

        public static void HtmlImagesDecodeAttributesAndInheritEnclosingStyle()
        {
            var runs = Parse("**[<IMG SRC='a&amp;b.png' ALT=diagram width=80 height=40 ALIGN=middle />](next.md)**")[0].Runs;
            var image = runs.Single(r => r.IsImage);
            AssertEx.Equal("a&b.png", image.ImagePath, "image URL entities decode");
            AssertEx.Equal("diagram", image.ImageAlt, "unquoted alt attribute");
            AssertEx.Equal("center", image.ImageAlign, "middle normalizes to center");
            AssertEx.Equal(80f, image.ImageWidth, "unquoted width");
            AssertEx.Equal(40f, image.ImageHeight, "unquoted height");
            AssertEx.True(image.Bold, "image inherits emphasis");
            AssertEx.Equal("/work/demo/docs/next.md", image.LocalLink, "image inherits enclosing anchor");
            image = Parse("prefix <img disabled src=figure.png style='color:red; FLOAT: right; broken'>")[0].Runs.Single(r => r.IsImage);
            AssertEx.Equal("right", image.ImageAlign, "float fallback ignores unrelated declarations and boolean attributes");
            image = Parse("prefix <img src=figure.png align=invalid>")[0].Runs.Single(r => r.IsImage);
            AssertEx.Equal("", image.ImageAlign, "unsupported alignment is ignored");
            AssertEx.True(Parse("prefix <img alt='missing source'>")[0].Runs.Any(r => r.Faint && r.Text.Contains("<img")), "invalid image remains visible markup");
        }

        public static void BlockConversionPreservesStructureAndSuppressesMetadata()
        {
            AssertEx.Equal(0, Parse(null).Count, "null document is empty");
            AssertEx.Equal(0, Parse("<!-- comment -->\n\n[ref]: next.md").Count, "comments and references are not visible blocks");
            var blocks = Parse("> quoted\n\n---\n\n    indented\n\n```rust\nlet x = 1;\n```\n\n<div>unsupported</div>\n\n<hr />");
            AssertEx.Sequence(new[] { BlockKind.Quote, BlockKind.Rule, BlockKind.Code, BlockKind.Code, BlockKind.Raw, BlockKind.Rule }, blocks.Select(b => b.Kind), "block kinds");
            AssertEx.Equal("quoted", Text(blocks[0].Children[0].Runs), "quote owns its paragraph");
            AssertEx.Equal("indented", blocks[2].Code, "indented code content");
            AssertEx.Equal("rust", blocks[3].Info, "fence language");
            AssertEx.Equal("let x = 1;", blocks[3].Code, "fence content");
            AssertEx.True(blocks[4].Runs[0].Faint && blocks[4].Runs[0].Text.Contains("unsupported"), "unsupported HTML block stays visible");
        }

        public static void TaskListsAndOrderedStartsSurviveParsing()
        {
            var list = Parse("- [x] done\n- [ ] todo")[0];
            var done = list.Children[0].Children[0].Runs[0];
            var todo = list.Children[1].Children[0].Runs[0];
            AssertEx.True(done.IsTask && done.TaskChecked, "checked task");
            AssertEx.True(todo.IsTask && !todo.TaskChecked, "unchecked task");
            AssertEx.Equal("[x] ", done.Text, "checked copy marker");
            AssertEx.Equal("[ ] ", todo.Text, "unchecked copy marker");
            list = Parse("7. seven\n8. eight")[0];
            AssertEx.True(list.Ordered, "ordered list");
            AssertEx.Equal(7, list.Start, "non-default start retained");
            AssertEx.Equal(1, Parse("0. zero")[0].Start, "zero start normalizes to one");
        }

        public static void ImageAltRetainsNestedTextAndCode()
        {
            var image = Parse("![a *nested* `code`\nline](figure.png)")[0].Runs[0];
            AssertEx.Equal("a nested code\nline", image.ImageAlt, "alt text flattens markup but retains source breaks");
        }

        public static void StandaloneHtmlImageAndTableAlignmentArePreserved()
        {
            var block = Parse("<img src='figure.png' alt='figure' width='40' />").Single();
            AssertEx.Equal(BlockKind.Paragraph, block.Kind, "standalone HTML image becomes a paragraph");
            AssertEx.True(block.Runs.Single().IsImage, "standalone HTML image is retained");
            var table = Parse("| left | default |\n| :--- | --- |\n| a | b |").Single();
            AssertEx.Equal(2, table.Rows[0].Cells.Count, "table has two visible columns");
            AssertEx.Sequence(new[] { TableAlignment.Left, TableAlignment.Left }, table.ColumnAlignments.Take(2), "explicit and implicit left alignment");
        }
    }
}
