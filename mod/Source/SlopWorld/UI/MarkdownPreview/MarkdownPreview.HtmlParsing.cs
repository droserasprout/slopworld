using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SlopWorld
{
    sealed partial class MarkdownDocumentParser
    {
        // Interpret the supported HTML block subset without enabling browser behavior.
        // Inline style/link/image policy stays shared with Markdown inline HTML.
        bool TryAddHtmlBlocks(string source, List<MarkdownBlock> target)
        {
            int firstEnd = HtmlTagEnd(source, 0);
            if (firstEnd < 0 || !TryParseHtmlTag(source.Substring(0, firstEnd + 1), out var first) ||
                first.Comment || first.Closing ||
                (!IsHtmlTextBlock(first.Name) && first.Name != "img" && first.Name != "hr")) return false;

            var state = new HtmlState();
            var block = HtmlParagraph();
            for (int at = 0; at < source.Length;)
            {
                int tagStart = source.IndexOf('<', at);
                if (tagStart < 0) tagStart = source.Length;
                string text = MarkdownMarkup.Decode(Regex.Replace(source.Substring(at, tagStart - at), @"[ \t\r\n]+", " "));
                AddRun(block.Runs, text,
                    new InlineStyle().WithHtmlState(state));
                if (tagStart == source.Length) break;

                int tagEnd = HtmlTagEnd(source, tagStart);
                if (tagEnd < 0)
                {
                    AddRun(block.Runs, source.Substring(tagStart), new InlineStyle(), faint: true);
                    break;
                }
                string markup = source.Substring(tagStart, tagEnd - tagStart + 1);
                if (TryParseHtmlTag(markup, out var tag) && !tag.Comment && IsHtmlTextBlock(tag.Name))
                {
                    AddHtmlTextBlock(block, target);
                    block = HtmlParagraph();
                    state = new HtmlState();
                    if (!tag.Closing)
                    {
                        block.Kind = tag.Name == "p" ? BlockKind.Paragraph : BlockKind.Heading;
                        block.Level = tag.Name == "p" ? 0 : tag.Name[1] - '0';
                        string alignment = ImageAlignment(tag.Attributes);
                        block.Alignment = alignment.Length == 0 ? null : alignment;
                    }
                }
                else if (TryParseHtmlTag(markup, out tag) && !tag.Comment && tag.Name == "hr")
                {
                    AddHtmlTextBlock(block, target);
                    target.Add(new MarkdownBlock { Kind = BlockKind.Rule });
                    block = HtmlParagraph();
                }
                else
                {
                    InlineStyle style = new InlineStyle().WithHtmlState(state);
                    if (!HandleHtmlTag(markup, block.Runs, state, style))
                        AddRun(block.Runs, markup, style, faint: true);
                }
                at = tagEnd + 1;
            }
            AddHtmlTextBlock(block, target);
            return true;
        }

        static MarkdownBlock HtmlParagraph() => new MarkdownBlock
        {
            Kind = BlockKind.Paragraph,
            Runs = new List<InlineRun>(),
        };

        static void AddHtmlTextBlock(MarkdownBlock block, List<MarkdownBlock> target)
        {
            // Formatting whitespace around a block does not create a visible paragraph.
            while (block.Runs.Count > 0 && !block.Runs[0].IsImage &&
                string.IsNullOrWhiteSpace(block.Runs[0].Text)) block.Runs.RemoveAt(0);
            while (block.Runs.Count > 0 && !block.Runs[block.Runs.Count - 1].IsImage &&
                string.IsNullOrWhiteSpace(block.Runs[block.Runs.Count - 1].Text))
                block.Runs.RemoveAt(block.Runs.Count - 1);
            if (block.Runs.Count > 0) target.Add(block);
        }

        static bool IsHtmlTextBlock(string name) => name == "p" ||
            (name.Length == 2 && name[0] == 'h' && name[1] >= '1' && name[1] <= '6');

        static int HtmlTagEnd(string source, int start)
        {
            if (source.IndexOf("<!--", start, StringComparison.Ordinal) == start)
            {
                int end = source.IndexOf("-->", start + 4, StringComparison.Ordinal);
                return end < 0 ? -1 : end + 2;
            }
            char quote = '\0';
            for (int at = start + 1; at < source.Length; at++)
            {
                char c = source[at];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                }
                else if (c == '\'' || c == '"') quote = c;
                else if (c == '>') return at;
            }
            return -1;
        }
    }
}
