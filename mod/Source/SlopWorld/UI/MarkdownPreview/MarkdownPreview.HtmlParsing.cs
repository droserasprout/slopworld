using System.Collections.Generic;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace SlopWorld
{
    sealed partial class MarkdownDocumentParser
    {
        // Parse strings directly: no loader, scripts, CSS engine, or resource fetching.
        // AngleSharp owns HTML syntax/tree repair; we own the supported display subset.
        readonly HtmlParser _htmlParser = new HtmlParser(new HtmlParserOptions { IsScripting = false });

        bool TryAddHtmlBlocks(string source, List<MarkdownBlock> target)
        {
            // Unsupported root blocks retain their original source as faint text.
            // Inspect the first token rather than guessing tag boundaries ourselves.
            using (var input = new AngleSharp.Text.TextSource((source ?? "").TrimStart()))
            using (var tokenizer = new AngleSharp.Html.Parser.HtmlTokenizer(input, AngleSharp.Html.HtmlEntityProvider.Resolver))
            {
                var first = tokenizer.Get();
                if (first.Type != HtmlTokenType.StartTag ||
                    (!IsHtmlTextBlock(first.Name) && first.Name != "img" && first.Name != "hr")) return false;
            }

            using (var document = _htmlParser.ParseDocument(""))
            {
                var nodes = _htmlParser.ParseFragment(source, document.Body);
                var block = HtmlParagraph();
                foreach (var node in nodes)
                    AppendHtmlNode(node, target, ref block, new InlineStyle());
                AddHtmlTextBlock(block, target);
            }
            return true;
        }

        void AppendHtmlNode(INode node, List<MarkdownBlock> target,
                            ref MarkdownBlock block, InlineStyle style)
        {
            if (node is IText text)
            {
                // The DOM has decoded entities once. Code keeps its literal whitespace.
                AddRun(block.Runs, style.Code ? text.Data : Regex.Replace(text.Data, @"[ \t\r\n]+", " "), style);
                return;
            }
            if (!(node is IElement element)) return; // Comments have no visible content.
            string name = element.LocalName;
            if (IsHtmlTextBlock(name))
            {
                AddHtmlTextBlock(block, target);
                block = HtmlParagraph();
                block.Kind = name == "p" ? BlockKind.Paragraph : BlockKind.Heading;
                block.Level = name == "p" ? 0 : name[1] - '0';
                string alignment = ImageAlignment(element.GetAttribute);
                block.Alignment = alignment.Length == 0 ? null : alignment;
                foreach (var child in element.ChildNodes)
                    AppendHtmlNode(child, target, ref block, style);
                AddHtmlTextBlock(block, target);
                block = HtmlParagraph();
                return;
            }
            if (name == "hr")
            {
                AddHtmlTextBlock(block, target);
                target.Add(new MarkdownBlock { Kind = BlockKind.Rule });
                block = HtmlParagraph();
                return;
            }
            if (name == "img")
            {
                var image = ParseImage(element.GetAttribute);
                if (image != null) block.Runs.Add(style.Apply(image));
                else AddRun(block.Runs, element.OuterHtml, style, faint: true);
                return;
            }
            if (name == "br")
            {
                AddRun(block.Runs, "\n", style);
                return;
            }

            if (!TryHtmlElementStyle(element, ref style))
            {
                // Unsupported elements are visible source, never active browser content.
                AddRun(block.Runs, element.OuterHtml, style, faint: true);
                return;
            }
            // Value styles naturally end at the DOM boundary, including repaired nesting.
            foreach (var child in element.ChildNodes)
                AppendHtmlNode(child, target, ref block, style);
        }

        enum HtmlStyleKind { Unsupported, Plain, Bold, Italic, Code, Strike, Link }

        // Tokenized inline tags and repaired DOM elements share this display policy.
        // Their callers retain ownership of counter scopes and DOM scopes respectively.
        static HtmlStyleKind HtmlStyleFor(string name)
        {
            switch (name)
            {
                case "strong":
                case "b": return HtmlStyleKind.Bold;
                case "em":
                case "i": return HtmlStyleKind.Italic;
                case "code":
                case "kbd":
                case "samp": return HtmlStyleKind.Code;
                case "del":
                case "s":
                case "strike": return HtmlStyleKind.Strike;
                case "a": return HtmlStyleKind.Link;
                // Common generated markup; CSS is deliberately not interpreted.
                case "span": return HtmlStyleKind.Plain;
                default: return HtmlStyleKind.Unsupported;
            }
        }

        InlineStyle ReadHtmlStyle(HtmlStyleKind kind, System.Func<string, string> attribute)
        {
            var effect = new InlineStyle
            {
                Bold = kind == HtmlStyleKind.Bold,
                Italic = kind == HtmlStyleKind.Italic,
                Code = kind == HtmlStyleKind.Code,
                InlineCode = kind == HtmlStyleKind.Code,
                Strike = kind == HtmlStyleKind.Strike,
            };
            if (kind == HtmlStyleKind.Link)
                _paths.TryResolveLink(attribute("href"), out effect.Link, out effect.LocalLink);
            return effect;
        }

        bool TryHtmlElementStyle(IElement element, ref InlineStyle style)
        {
            var kind = HtmlStyleFor(element.LocalName);
            if (kind == HtmlStyleKind.Unsupported) return false;
            var effect = ReadHtmlStyle(kind, element.GetAttribute);
            if (kind == HtmlStyleKind.Link) style = style.WithLink(effect.Link, effect.LocalLink);
            style = style.WithHtmlStyle(effect);
            return true;
        }

        static MarkdownBlock HtmlParagraph() => new MarkdownBlock
        {
            Kind = BlockKind.Paragraph,
            Runs = new List<InlineRun>(),
        };

        static void AddHtmlTextBlock(MarkdownBlock block, List<MarkdownBlock> target)
        {
            // Source formatting around wrappers is invisible; explicit br runs survive.
            while (block.Runs.Count > 0 && IsHtmlFormattingSpace(block.Runs[0]))
                block.Runs.RemoveAt(0);
            while (block.Runs.Count > 0 && IsHtmlFormattingSpace(block.Runs[block.Runs.Count - 1]))
                block.Runs.RemoveAt(block.Runs.Count - 1);
            if (block.Runs.Count > 0) target.Add(block);
        }

        static bool IsHtmlFormattingSpace(InlineRun run) => !run.IsImage && !run.Code &&
            run.Text != "\n" && string.IsNullOrWhiteSpace(run.Text);

        static bool IsHtmlTextBlock(string name) => name == "p" ||
            (name.Length == 2 && name[0] == 'h' && name[1] >= '1' && name[1] <= '6');
    }
}
