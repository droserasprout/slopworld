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
            foreach (var dimension in new[] { (Input: "32px", ExpectedWidth: 32f), (Input: "12.5", ExpectedWidth: 12.5f), (Input: "50%", ExpectedWidth: 0f), (Input: "-1", ExpectedWidth: 0f), (Input: "bad", ExpectedWidth: 0f) })
                yield return ("HTML image dimension: " + dimension.Input, () =>
                {
                    var image = Parse("before <img src='figure.png' width='" + dimension.Input + "' height='16PX'>")[0].Runs.Single(r => r.IsImage);
                    AssertEx.Equal(dimension.ExpectedWidth, image.ImageWidth, "absolute positive dimensions only");
                    AssertEx.Equal(16f, image.ImageHeight, "pixel suffix is case insensitive");
                }
                );
        }

        static void HtmlStyleScope(string tag)
        {
            var runs = Parse("before <" + tag + ">outer <" + tag + ">inner</" + tag + "> tail</" + tag + "> after")[0].Runs;
            int offset = 0;
            foreach (var run in runs)
            {
                int end = offset + run.Text.Length;
                bool styled = offset >= "before ".Length && end <= "before outer inner tail".Length;
                AssertEx.True(styled || end <= "before ".Length || offset >= "before outer inner tail".Length, "run does not cross a style boundary");
                offset = end;
                AssertEx.Equal(styled && (tag == "b" || tag == "strong"), run.Bold, "bold scope");
                AssertEx.Equal(styled && (tag == "i" || tag == "em"), run.Italic, "italic scope");
                bool code = styled && (tag == "code" || tag == "kbd" || tag == "samp");
                AssertEx.Equal(code, run.Code, "code scope");
                AssertEx.Equal(code, run.InlineCode, "inline code chip scope");
                AssertEx.Equal(styled && (tag == "del" || tag == "s" || tag == "strike"), run.Strike, "strike scope");
            }
            AssertEx.Equal("before outer inner tail after", Text(runs), "supported tags disappear without losing text");
        }

        public static void SoftBreaksBecomeSpacesAndExplicitBreaksRemain()
        {
            AssertEx.Equal("one two three", Text(Parse("one\ntwo\n  three")[0].Runs),
                "ordinary source newlines and continuation indentation do not force a line break");
            AssertEx.Equal("one\ntwo", Text(Parse("one  \ntwo")[0].Runs), "two trailing spaces force a break");
            AssertEx.Equal("one\ntwo", Text(Parse("one\\\ntwo")[0].Runs), "backslash forces a break");
            AssertEx.Equal("one\ntwo", Text(Parse("one<br>two")[0].Runs), "HTML br forces a break");
            AssertEx.Equal(2, Parse("one\n\ntwo").Count, "blank line still separates paragraphs");
        }

        public static void HtmlCodePreservesLiteralWhitespaceWithinBlocks()
        {
            foreach (string tag in new[] { "code", "kbd", "samp" })
            {
                string markup = "<" + tag + ">a  b</" + tag + ">";
                var inline = Parse("prefix " + markup)[0].Runs.Single(r => r.Code);
                var block = Parse("<p>prefix " + markup + "</p>")[0].Runs.Single(r => r.Code);
                AssertEx.Equal(inline.Text, block.Text, "block and inline HTML code retain the same spaces");
                block = Parse("<p><" + tag + "> a\t b\n c </" + tag + "></p>")[0].Runs.Single();
                AssertEx.Equal(" a\t b\n c ", block.Text, "code retains tabs, newlines, and edge spaces");
                block = Parse("<p><" + tag + ">  </" + tag + "></p>")[0].Runs.Single();
                AssertEx.Equal("  ", block.Text, "whitespace-only code is visible content");
                AssertEx.True(block.Code && block.InlineCode, "literal text keeps code styling");
            }
            AssertEx.Equal("a b", Text(Parse("<p>a  b</p>")[0].Runs), "ordinary HTML still collapses whitespace");
        }

        public static void InlineHtmlCodePreservesNewlinesAndEndsAtClosingTag()
        {
            foreach (string tag in new[] { "code", "kbd", "samp" })
            {
                var runs = Parse("prefix <" + tag + ">a\nb</" + tag + "> after\nnext").Single().Runs;
                AssertEx.Equal("a\nb", Text(runs.Where(r => r.Code)), "HTML code retains literal source newline");
                AssertEx.Equal("prefix a\nb after next", Text(runs), "ordinary soft breaks resume after closing code tag");
            }
        }

        public static void InlineAndBlockHtmlShareDisplayStylePolicy()
        {
            foreach (string tag in new[] { "b", "strong", "i", "em", "code", "kbd", "samp", "del", "s", "strike", "span", "a" })
            {
                string markup = "<" + tag + " href='next.md'>styled</" + tag + ">";
                var inline = Parse("prefix " + markup + " after").Single().Runs.Single(r => r.Text == "styled");
                var block = Parse("<p>prefix " + markup + " after</p>").Single().Runs.Single(r => r.Text == "styled");
                AssertEx.Equal((inline.Bold, inline.Italic, inline.Code, inline.InlineCode, inline.Strike),
                    (block.Bold, block.Italic, block.Code, block.InlineCode, block.Strike), "shared effects for " + tag);
                AssertEx.Equal(inline.Link, block.Link, "shared external link policy for " + tag);
                AssertEx.Equal(inline.LocalLink, block.LocalLink, "shared scoped link policy for " + tag);
            }
        }

        public static void HtmlParagraphsAndAllHeadingLevelsBecomeStyledBlocks()
        {
            var blocks = Parse("<p align='center'>\n<img src='logo.png' width='32' height='32'>\n</p>\n\n<h1 align='center'>SlopWorld</h1>");
            AssertEx.Equal(2, blocks.Count, "logo and title have separate blocks");
            AssertEx.Equal(BlockKind.Paragraph, blocks[0].Kind, "p becomes paragraph");
            AssertEx.Equal("center", blocks[0].Alignment, "paragraph alignment retained");
            AssertEx.True(blocks[0].Runs.Single().IsImage, "wrapper whitespace is suppressed");
            AssertEx.Equal(BlockKind.Heading, blocks[1].Kind, "h1 becomes heading");
            AssertEx.Equal("center", blocks[1].Alignment, "heading alignment retained");
            AssertEx.Equal("SlopWorld", Text(blocks[1].Runs), "heading tags are hidden");
            for (int level = 1; level <= 6; level++)
            {
                var heading = Parse("<h" + level + " align='right'><b>Title &amp; more</b></h" + level + ">")[0];
                AssertEx.Equal(level, heading.Level, "HTML heading level retained");
                AssertEx.Equal("Title & more", Text(heading.Runs), "text decodes once");
                AssertEx.True(heading.Runs.All(r => r.Bold), "inline styles survive in HTML blocks");
            }
            blocks = Parse("<p>first\nline</p><p><a href='next.md'>next</a><br>last</p>");
            AssertEx.Equal(2, blocks.Count, "adjacent p elements split into paragraphs");
            AssertEx.Equal("first line", Text(blocks[0].Runs), "HTML newlines collapse");
            AssertEx.Equal("next\nlast", Text(blocks[1].Runs), "HTML br retained in block");
            AssertEx.Equal("/work/demo/docs/next.md", blocks[1].Runs[0].LocalLink, "HTML link uses scoped resolver");
        }

        public static void AdjacentHtmlImagesAndParagraphsRetainAllContent()
        {
            var blocks = Parse("<img src='shot.png' align='right'>\n<p>body &nbsp; text</p>\n<h2>Next</h2>");
            AssertEx.Equal(3, blocks.Count, "an HTML block may contain image, paragraph, and heading");
            AssertEx.True(blocks[0].Runs.Single().IsImage, "standalone image retained");
            AssertEx.Equal("body \u00a0 text", Text(blocks[1].Runs), "nonbreaking entity space retained");
            AssertEx.Equal(BlockKind.Heading, blocks[2].Kind, "following heading retained");
            AssertEx.Equal(null, blocks[1].Alignment, "absent alignment preserves default image policy");
        }

        public static void HtmlBlockTokenizerPreservesUnknownTagsAndQuotedAngles()
        {
            var blocks = Parse("<p title='a > b'><!-- hidden --><img src='a>b.png'><unknown>x</unknown></p>");
            AssertEx.Equal("a>b.png", blocks[0].Runs.Single(r => r.IsImage).ImagePath,
                "quoted angle does not terminate a tag");
            AssertEx.Equal("<unknown>x</unknown>", Text(blocks[0].Runs), "unknown markup stays visible");
            AssertEx.True(blocks[0].Runs.Any(r => r.Faint), "unknown markup stays faint");
        }

        public static void HtmlLiteralAnglesAndEntitiesRetainText()
        {
            var blocks = Parse("<p>a < b &amp; c &amp;lt; &CounterClockwiseContourIntegral;</p>");
            AssertEx.Equal("a < b & c &lt; ∳", Text(blocks.Single().Runs),
                "HTML text uses standard tokenization and decodes entities once");
            var image = Parse("<p><img src='a&amp;lt;>b.png' alt='&amp;lt;'></p>")[0].Runs.Single();
            AssertEx.Equal("a&lt;>b.png", image.ImagePath, "DOM image URL decodes once");
            AssertEx.Equal("&lt;", image.ImageAlt, "DOM image alt decodes once");
            var runs = Parse("prefix <a href='https://example.test/a>b?x=&amp;lt;' title='x > y'>link</a>")[0].Runs;
            AssertEx.Equal("https://example.test/a>b?x=&lt;", runs.Single(r => r.Text == "link").Link,
                "inline tokenizer shares quoted-attribute and entity handling");
        }

        public static void HtmlTreeRepairKeepsParagraphsAndStyleBoundaries()
        {
            var blocks = Parse("<p>first<p>second</p><h2>third</h2>");
            AssertEx.Sequence(new[] { "first", "second", "third" }, blocks.Select(b => Text(b.Runs)),
                "omitted paragraph closing tag is repaired");
            blocks = Parse("<p><b>bold<i>both</b>italic</i> plain</p>");
            var runs = blocks.Single().Runs;
            AssertEx.True(runs.Single(r => r.Text == "both").Bold && runs.Single(r => r.Text == "both").Italic,
                "misnested formatting keeps combined style");
            AssertEx.False(runs.Single(r => r.Text == "italic").Bold, "repaired italic does not inherit closed bold");
            AssertEx.True(runs.Single(r => r.Text == "italic").Italic, "repaired italic remains active");
            AssertEx.False(runs.Single(r => r.Text == " plain").Italic, "style ends at repaired DOM boundary");
            AssertEx.Equal("\nbody\n", Text(Parse("<p><br>body<br></p>").Single().Runs),
                "explicit breaks survive wrapper whitespace trimming");
        }

        public static void HtmlParsingRetainsPassiveContentAndScopedLinks()
        {
            var runs = Parse("<p onclick='alert(1)'><a href='../../escape.md'>escape</a><img src='https://example.test/remote.png'><script>alert(1)</script><!-- hidden --></p>")[0].Runs;
            AssertEx.Equal(null, runs.Single(r => r.Text == "escape").LocalLink, "HTML links cannot escape project scope");
            AssertEx.Equal("https://example.test/remote.png", runs.Single(r => r.IsImage).ImagePath,
                "parser records image source for resource owner's policy");
            AssertEx.True(runs.Any(r => r.Faint && r.Text.Contains("<script>")), "unsupported script remains passive source");
            AssertEx.False(Text(runs).Contains("hidden") || Text(runs).Contains("onclick"), "comments and unsupported attributes are hidden");
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
            AssertEx.Equal("a nested code line", image.ImageAlt, "alt text flattens markup and soft breaks");
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
        public static void ListTightness()
        {
            var tight = PreviewParser().Parse("- one\n- two")[0];
            var loose = PreviewParser().Parse("- one\n\n- two")[0];
            AssertEx.True(tight.Kind == BlockKind.List && tight.Tight,
                "ordinary list is tight");
            AssertEx.True(loose.Kind == BlockKind.List && !loose.Tight,
                "blank item separation preserves loose list semantics");
        }

        public static void SixHeadingLevels()
        {
            var blocks = PreviewParser().Parse("# one\n\n## two\n\n### three\n\n#### four\n\n##### five\n\n###### six");
            AssertEx.Equal(6, blocks.Count, "all heading blocks are retained");
            for (int i = 0; i < blocks.Count; i++)
                AssertEx.Equal(i + 1, blocks[i].Level, "heading level " + (i + 1));
        }

        public static void ImagesKeepAltText()
        {
            var blocks = PreviewParser().Parse("![diagram](diagram.png)\n\n![][missing]\n\n[missing]: empty.png");
            var first = blocks[0].Runs[0];
            var second = blocks[1].Runs[0];

            AssertEx.True(first.IsImage, "ordinary markdown image is an image run");
            AssertEx.Equal("diagram", first.ImageAlt, "ordinary image keeps alt text");
            AssertEx.Equal("diagram.png", first.ImagePath, "ordinary image keeps source path");
            AssertEx.True(second.IsImage, "reference markdown image is an image run");
            AssertEx.Equal("", second.ImageAlt, "empty alt text is retained exactly");
            AssertEx.Equal("empty.png", second.ImagePath, "reference image resolves its definition");
        }

        public static void LinkedImagesKeepLinks()
        {
            var blocks = PreviewParser().Parse("[![diagram](diagram.png)](other.md)");
            var image = blocks[0].Runs[0];

            AssertEx.True(image.IsImage, "linked image remains an image run");
            AssertEx.Equal("diagram", image.ImageAlt, "linked image keeps alt text");
            AssertEx.Equal("/work/demo/docs/other.md", image.LocalLink,
                "enclosing relative link is retained");
            AssertEx.Equal(null, image.Link, "local image link has no external URL");
        }

        static MarkdownDocumentParser PreviewParser()
        {
            var paths = new MarkdownPathResolver("demo", "/work/demo/docs/readme.md",
                () => "/work/demo");
            return new MarkdownDocumentParser(paths);
        }

    }
}
