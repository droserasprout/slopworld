using System;
using System.Collections.Generic;
using System.Globalization;
using AngleSharp.Html;
using AngleSharp.Html.Parser;
using AngleSharp.Html.Parser.Tokens;
using AngleSharp.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using UnityEngine;

namespace SlopWorld
{
    sealed partial class MarkdownDocumentParser
    {
        readonly MarkdownPathResolver _paths;

        public MarkdownDocumentParser(MarkdownPathResolver paths)
        {
            _paths = paths;
        }

        struct InlineStyle
        {
            public bool Bold;
            public bool Italic;
            public bool Code;
            public bool InlineCode;
            public bool Strike;
            public string Link;
            public string LocalLink;

            public InlineRun Apply(InlineRun run)
            {
                run.Bold = Bold;
                run.Italic = Italic;
                run.Code = Code;
                run.InlineCode = InlineCode;
                run.Strike = Strike;
                run.Link = Link;
                run.LocalLink = LocalLink;
                return run;
            }

            public InlineStyle WithHtmlState(HtmlState state)
            {
                return WithHtmlStyle(new InlineStyle
                {
                    Bold = state.Bold > 0,
                    Italic = state.Italic > 0,
                    Code = state.Code > 0,
                    InlineCode = state.InlineCode > 0,
                    Strike = state.Strike > 0,
                    Link = state.Link,
                    LocalLink = state.LocalLink,
                });
            }

            public InlineStyle WithHtmlStyle(InlineStyle effect)
            {
                var result = this;
                result.Bold |= effect.Bold;
                result.Italic |= effect.Italic;
                result.Code |= effect.Code;
                result.InlineCode |= effect.InlineCode;
                result.Strike |= effect.Strike;
                result.Link = result.Link ?? effect.Link;
                result.LocalLink = result.LocalLink ?? effect.LocalLink;
                return result;
            }

            public InlineStyle WithEmphasis(bool strong)
            {
                var result = this;
                result.Bold = result.Bold || strong;
                result.Italic = result.Italic || !strong;
                return result;
            }

            public InlineStyle WithCode()
            {
                var result = this;
                result.Code = true;
                result.InlineCode = true;
                return result;
            }

            public InlineStyle WithLink(string link, string localLink)
            {
                var result = this;
                result.Link = link ?? result.Link;
                result.LocalLink = localLink ?? result.LocalLink;
                return result;
            }
        }

        sealed class HtmlState
        {
            public int Bold;
            public int Italic;
            public int Code;
            public int InlineCode;
            public int Strike;
            public string Link;
            public string LocalLink;

            public void ApplyStyle(HtmlStyleKind kind, InlineStyle effect, bool closing)
            {
                int delta = closing ? -1 : 1;
                if (effect.Bold) Bold = Math.Max(0, Bold + delta);
                if (effect.Italic) Italic = Math.Max(0, Italic + delta);
                if (effect.Code) Code = Math.Max(0, Code + delta);
                if (effect.InlineCode) InlineCode = Math.Max(0, InlineCode + delta);
                if (effect.Strike) Strike = Math.Max(0, Strike + delta);
                if (kind == HtmlStyleKind.Link)
                {
                    Link = closing ? null : effect.Link;
                    LocalLink = closing ? null : effect.LocalLink;
                }
            }
        }

        sealed class HtmlTagInfo
        {
            public string Name;
            public Func<string, string> Attribute;
            public bool Closing;
            public bool Comment;
        }

        static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseGridTables()
            .UseTaskLists()
            .UseAutoLinks()
            .Build();

        public List<MarkdownBlock> Parse(string source)
        {
            var document = Markdown.Parse(source ?? "", Pipeline, null);
            var blocks = new List<MarkdownBlock>();
            AddBlocks(document, blocks);
            return blocks;
        }

        void AddBlocks(ContainerBlock container, List<MarkdownBlock> target)
        {
            foreach (Block block in container)
            {
                if (block is HtmlBlock html && TryAddHtmlBlocks(html.Lines.ToString(), target)) continue;
                var converted = ConvertBlock(block);
                if (converted != null) target.Add(converted);
            }
        }

        MarkdownBlock ConvertBlock(Block block)
        {
            switch (block)
            {
                case BlankLineBlock blank:
                    return ConvertBlankLine(blank);
                case LinkReferenceDefinition linkReference:
                    return ConvertLinkReference(linkReference);
                // Markdig groups reference definitions in a container. It is metadata too.
                // converting it as a generic container would draw an empty quote.
                case LinkReferenceDefinitionGroup _:
                    return null;
                case HeadingBlock heading:
                    return ConvertHeading(heading);
                case ParagraphBlock paragraph:
                    return ConvertParagraph(paragraph);
                case FencedCodeBlock fenced:
                    return ConvertFencedCode(fenced);
                case CodeBlock code:
                    return ConvertCode(code);
                case QuoteBlock quote:
                    return ConvertQuote(quote);
                case ListBlock list:
                    return ConvertList(list);
                case ListItemBlock item:
                    return ConvertListItem(item);
                case ThematicBreakBlock rule:
                    return ConvertThematicBreak(rule);
                case Table table:
                    return ConvertTable(table);
                case HtmlBlock html:
                    return ConvertHtml(html);
                case ContainerBlock container:
                    return ConvertContainer(container);
                default:
                    return ConvertRaw(block);
            }
        }

        MarkdownBlock ConvertBlankLine(BlankLineBlock block)
        {
            return null;
        }

        MarkdownBlock ConvertLinkReference(LinkReferenceDefinition block)
        {
            // AutoIdentifiers stores heading anchors as document-level link definitions.
            // They are parser metadata, not visible blocks. Stringifying them would leak
            // names such as HeadingLinkReferenceDefinition into the preview.
            return null;
        }

        MarkdownBlock ConvertHeading(HeadingBlock heading)
        {
            return new MarkdownBlock
            {
                Kind = BlockKind.Heading,
                Level = Mathf.Clamp(heading.Level, 1, 6),
                Runs = ReadInlines(heading.Inline),
            };
        }

        MarkdownBlock ConvertParagraph(ParagraphBlock paragraph)
        {
            return new MarkdownBlock
            {
                Kind = BlockKind.Paragraph,
                Runs = ReadInlines(paragraph.Inline),
            };
        }

        MarkdownBlock ConvertFencedCode(FencedCodeBlock fenced)
        {
            return new MarkdownBlock
            {
                Kind = BlockKind.Code,
                Code = fenced.Lines.ToString(),
                Info = fenced.Info,
            };
        }

        MarkdownBlock ConvertCode(CodeBlock code)
        {
            return new MarkdownBlock { Kind = BlockKind.Code, Code = code.Lines.ToString() };
        }

        MarkdownBlock ConvertQuote(QuoteBlock quote)
        {
            var children = new List<MarkdownBlock>();
            AddBlocks(quote, children);
            return new MarkdownBlock { Kind = BlockKind.Quote, Children = children };
        }

        MarkdownBlock ConvertList(ListBlock list)
        {
            var children = new List<MarkdownBlock>();
            AddBlocks(list, children);
            if (!int.TryParse(list.OrderedStart, out int start)) start = 1;
            return new MarkdownBlock
            {
                Kind = BlockKind.List,
                Ordered = list.IsOrdered,
                Tight = !list.IsLoose,
                Start = start < 1 ? 1 : start,
                Children = children,
            };
        }

        MarkdownBlock ConvertListItem(ListItemBlock item)
        {
            var children = new List<MarkdownBlock>();
            AddBlocks(item, children);
            return new MarkdownBlock { Kind = BlockKind.Item, Children = children };
        }

        MarkdownBlock ConvertThematicBreak(ThematicBreakBlock rule)
        {
            return new MarkdownBlock { Kind = BlockKind.Rule };
        }

        MarkdownBlock ConvertTable(Table table)
        {
            var rows = new List<TableRow>();
            foreach (Block child in table)
            {
                if (!(child is Markdig.Extensions.Tables.TableRow row)) continue;
                var output = new TableRow { Header = row.IsHeader };
                foreach (Block cellBlock in row)
                {
                    if (!(cellBlock is TableCell cell)) continue;
                    output.Cells.Add(ReadCell(cell));
                }
                rows.Add(output);
            }
            var alignments = new List<TableAlignment>();
            foreach (var definition in table.ColumnDefinitions)
            {
                if (!definition.Alignment.HasValue)
                {
                    alignments.Add(TableAlignment.Left);
                    continue;
                }
                switch (definition.Alignment.Value)
                {
                    case TableColumnAlign.Center:
                        alignments.Add(TableAlignment.Center);
                        break;
                    case TableColumnAlign.Right:
                        alignments.Add(TableAlignment.Right);
                        break;
                    default:
                        alignments.Add(TableAlignment.Left);
                        break;
                }
            }
            return new MarkdownBlock
            {
                Kind = BlockKind.Table,
                Rows = rows,
                ColumnAlignments = alignments,
            };
        }

        MarkdownBlock ConvertHtml(HtmlBlock html)
        {
            string source = html.Lines.ToString();
            if (IsHtmlComment(source)) return null;
            if (IsHtmlRule(source)) return new MarkdownBlock { Kind = BlockKind.Rule };

            return new MarkdownBlock
            {
                Kind = BlockKind.Raw,
                Runs = new List<InlineRun>
                {
                    new InlineRun { Text = source, Faint = true },
                },
            };
        }

        MarkdownBlock ConvertContainer(ContainerBlock container)
        {
            var children = new List<MarkdownBlock>();
            AddBlocks(container, children);
            return new MarkdownBlock { Kind = BlockKind.Quote, Children = children };
        }

        MarkdownBlock ConvertRaw(Block block)
        {
            return new MarkdownBlock
            {
                Kind = BlockKind.Raw,
                Runs = new List<InlineRun>
                {
                    new InlineRun { Text = block.ToString(), Faint = true },
                },
            };
        }

        List<InlineRun> ReadCell(TableCell cell)
        {
            var runs = new List<InlineRun>();
            foreach (Block child in cell)
            {
                if (child is ParagraphBlock paragraph)
                    AppendInlines(paragraph.Inline, runs, new InlineStyle(), new HtmlState());
            }
            return runs;
        }

        List<InlineRun> ReadInlines(ContainerInline inline)
        {
            var runs = new List<InlineRun>();
            AppendInlines(inline, runs, new InlineStyle(), new HtmlState());
            return runs;
        }

        void AppendInlines(ContainerInline container, List<InlineRun> target,
                           InlineStyle style, HtmlState htmlState)
        {
            if (container == null) return;
            foreach (Inline inline in container)
            {
                InlineStyle currentStyle = style.WithHtmlState(htmlState);

                if (inline is LiteralInline literal)
                {
                    AddRun(target, literal.Content.ToString(), currentStyle);
                }
                else if (inline is HtmlEntityInline entity)
                {
                    AddRun(target, entity.Transcoded.ToString(), currentStyle);
                }
                else if (inline is CodeInline codeInline)
                {
                    AddRun(target, codeInline.Content, currentStyle.WithCode());
                }
                else if (inline is EmphasisInline emphasis)
                {
                    bool strong = emphasis.DelimiterCount >= 2;
                    AppendInlines(emphasis, target, currentStyle.WithEmphasis(strong), htmlState);
                }
                else if (inline is LinkInline linkInline)
                {
                    if (linkInline.IsImage)
                    {
                        AppendImage(linkInline, target, currentStyle);
                    }
                    else
                    {
                        _paths.TryResolveLink(linkInline.Url, out var url, out var local);
                        AppendInlines(linkInline, target, currentStyle.WithLink(url, local), htmlState);
                    }
                }
                else if (inline is AutolinkInline auto)
                {
                    _paths.TryResolveLink(auto.Url, out var url, out var local);
                    AddRun(target, auto.Url, currentStyle.WithLink(url, local));
                }
                else if (inline is TaskList task)
                {
                    target.Add(currentStyle.Apply(new InlineRun
                    {
                        Text = task.Checked ? "[x] " : "[ ] ",
                        IsTask = true,
                        TaskChecked = task.Checked,
                    }));
                }
                else if (inline is LineBreakInline lineBreak)
                {
                    AddRun(target, lineBreak.IsHard || currentStyle.Code ? "\n" : " ", currentStyle);
                }
                else if (inline is HtmlInline html)
                {
                    if (!HandleHtmlTag(html.Tag, target, htmlState, currentStyle))
                        AddRun(target, html.Tag, currentStyle, faint: true);
                }
                else if (inline is ContainerInline nested)
                {
                    AppendInlines(nested, target, currentStyle, htmlState);
                }
                else
                {
                    AddRun(target, inline.ToString(), currentStyle, faint: true);
                }
            }
        }

        // Markdig supplies individual inline tags, so tokenize those without building
        // a DOM that would discard unmatched closing tags. HTML blocks use tree parsing.
        static bool TryParseHtmlTag(string source, out HtmlTagInfo tag)
        {
            tag = null;
            using (var input = new TextSource((source ?? "").Trim()))
            using (var tokenizer = new HtmlTokenizer(input, HtmlEntityProvider.Resolver))
            {
                var token = tokenizer.Get();
                if (tokenizer.Get().Type != HtmlTokenType.EndOfFile) return false;
                if (token.Type == HtmlTokenType.Comment)
                {
                    tag = new HtmlTagInfo { Comment = true };
                    return true;
                }
                if (!(token is HtmlTagToken htmlTag)) return false;
                tag = new HtmlTagInfo
                {
                    Name = htmlTag.Name,
                    Attribute = htmlTag.GetAttribute,
                    Closing = token.Type == HtmlTokenType.EndTag,
                };
                return true;
            }
        }

        static bool IsHtmlRule(string source)
        {
            return TryParseHtmlTag(source, out var tag) && !tag.Comment && !tag.Closing &&
                tag.Name == "hr";
        }

        static bool IsHtmlComment(string source)
        {
            return TryParseHtmlTag(source, out var tag) && tag.Comment;
        }

        bool HandleHtmlTag(string source, List<InlineRun> target, HtmlState state,
                           InlineStyle style)
        {
            if (!TryParseHtmlTag(source, out var tag)) return false;
            if (tag.Comment) return true;

            string name = tag.Name;
            if (name == "img")
            {
                var image = ParseImage(tag.Attribute);
                if (image == null || tag.Closing) return false;
                style.Apply(image);
                target.Add(image);
                return true;
            }

            if (name == "br")
            {
                if (!tag.Closing)
                    AddRun(target, "\n", style);
                return true;
            }

            var kind = HtmlStyleFor(name);
            if (kind == HtmlStyleKind.Unsupported) return false;
            state.ApplyStyle(kind, ReadHtmlStyle(kind, tag.Attribute), tag.Closing);
            return true;
        }

        void AppendImage(LinkInline image, List<InlineRun> target, InlineStyle style)
        {
            if (image == null || string.IsNullOrWhiteSpace(image.Url)) return;
            target.Add(style.Apply(new InlineRun
            {
                IsImage = true,
                ImagePath = image.Url,
                ImageAlt = InlineText(image),
            }));
        }

        static string InlineText(ContainerInline container)
        {
            var text = new System.Text.StringBuilder();
            AppendInlineText(container, text);
            return text.ToString();
        }

        static void AppendInlineText(Inline inline, System.Text.StringBuilder target)
        {
            if (inline == null) return;
            if (inline is LiteralInline literal)
            {
                target.Append(literal.Content.ToString());
                return;
            }
            if (inline is HtmlEntityInline entity)
            {
                target.Append(entity.Transcoded.ToString());
                return;
            }
            if (inline is CodeInline code)
            {
                target.Append(code.Content);
                return;
            }
            if (inline is LineBreakInline lineBreak)
            {
                target.Append(lineBreak.IsHard ? '\n' : ' ');
                return;
            }
            if (inline is ContainerInline container)
                foreach (Inline child in container)
                    AppendInlineText(child, target);
        }

        // Both AngleSharp DOM attributes and token attributes are already decoded.
        static InlineRun ParseImage(Func<string, string> attribute)
        {
            string source = attribute("src");
            if (string.IsNullOrWhiteSpace(source)) return null;
            return new InlineRun
            {
                IsImage = true,
                ImagePath = source,
                ImageAlt = attribute("alt") ?? "",
                ImageWidth = HtmlDimension(attribute("width")),
                ImageHeight = HtmlDimension(attribute("height")),
                ImageAlign = ImageAlignment(attribute),
            };
        }

        static string ImageAlignment(Func<string, string> attribute)
        {
            string align = (attribute("align") ?? "").Trim().ToLowerInvariant();
            if (align.Length == 0)
                align = (CssProperty(attribute("style"), "float") ?? "").Trim().ToLowerInvariant();
            if (align == "middle") align = "center";
            return align == "left" || align == "right" || align == "center" ? align : "";
        }

        static string CssProperty(string style, string property)
        {
            foreach (string declaration in (style ?? "").Split(';'))
            {
                int colon = declaration.IndexOf(':');
                if (colon < 0) continue;
                string name = declaration.Substring(0, colon).Trim();
                if (!string.Equals(name, property, StringComparison.OrdinalIgnoreCase)) continue;
                return declaration.Substring(colon + 1).Trim();
            }
            return null;
        }

        static float HtmlDimension(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0f;
            value = value.Trim();
            if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - 2);
            if (value.EndsWith("%", StringComparison.Ordinal)) return 0f;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var result) && result > 0f ? result : 0f;
        }

        static void AddRun(List<InlineRun> target, string text, InlineStyle style,
                           bool faint = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            target.Add(style.Apply(new InlineRun
            {
                Text = text,
                Faint = faint,
            }));
        }
    }
}
