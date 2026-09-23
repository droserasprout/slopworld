using System;
using System.Collections.Generic;
using System.Globalization;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using UnityEngine;

namespace SlopWorld
{
    sealed class MarkdownDocumentParser
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

            public InlineStyle WithHtmlState(HtmlState state)
            {
                var result = this;
                result.Bold = result.Bold || state.Bold > 0;
                result.Italic = result.Italic || state.Italic > 0;
                result.Code = result.Code || state.Code > 0;
                result.InlineCode = result.InlineCode || state.InlineCode > 0;
                result.Strike = result.Strike || state.Strike > 0;
                result.Link = result.Link ?? state.Link;
                result.LocalLink = result.LocalLink ?? state.LocalLink;
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
        }

        sealed class HtmlTagInfo
        {
            public string Name;
            public string Attributes;
            public bool Closing;
            public bool SelfClosing;
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
            int start = 1;
            int.TryParse(list.OrderedStart, out start);
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
            var image = ParseImage(source);
            if (image != null)
                return new MarkdownBlock
                {
                    Kind = BlockKind.Paragraph,
                    Runs = new List<InlineRun> { image },
                };

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
                    AddRun(target, MarkdownMarkup.Decode(literal.Content.ToString()), currentStyle);
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
                    AddRun(target, MarkdownMarkup.Decode(auto.Url), currentStyle.WithLink(url, local));
                }
                else if (inline is TaskList task)
                {
                    target.Add(new InlineRun
                    {
                        Text = task.Checked ? "[x] " : "[ ] ",
                        Bold = currentStyle.Bold,
                        Italic = currentStyle.Italic,
                        Code = currentStyle.Code,
                        InlineCode = currentStyle.InlineCode,
                        Strike = currentStyle.Strike,
                        IsTask = true,
                        TaskChecked = task.Checked,
                        Link = currentStyle.Link,
                        LocalLink = currentStyle.LocalLink,
                    });
                }
                else if (inline is LineBreakInline)
                {
                    AddRun(target, "\n", currentStyle);
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

        static bool TryParseHtmlTag(string source, out HtmlTagInfo tag)
        {
            tag = null;
            source = (source ?? "").Trim();
            if (source.Length < 3 || source[0] != '<' || source[source.Length - 1] != '>')
                return false;
            if (source.StartsWith("<!--", StringComparison.Ordinal) &&
                source.EndsWith("-->", StringComparison.Ordinal))
            {
                tag = new HtmlTagInfo { Comment = true };
                return true;
            }

            int end = source.Length - 1;
            int at = 1;
            while (at < end && char.IsWhiteSpace(source[at])) at++;
            bool closing = at < end && source[at] == '/';
            if (closing) at++;
            while (at < end && char.IsWhiteSpace(source[at])) at++;
            int nameStart = at;
            while (at < end && IsHtmlNameChar(source[at])) at++;
            if (at == nameStart) return false;

            int attrEnd = end;
            while (attrEnd > at && char.IsWhiteSpace(source[attrEnd - 1])) attrEnd--;
            bool selfClosing = attrEnd > at && source[attrEnd - 1] == '/';
            if (selfClosing) attrEnd--;
            while (attrEnd > at && char.IsWhiteSpace(source[attrEnd - 1])) attrEnd--;

            tag = new HtmlTagInfo
            {
                Name = source.Substring(nameStart, at - nameStart).ToLowerInvariant(),
                Attributes = source.Substring(at, Mathf.Max(0, attrEnd - at)).Trim(),
                Closing = closing,
                SelfClosing = selfClosing,
            };
            return true;
        }

        static bool IsHtmlNameChar(char c) =>
            char.IsLetterOrDigit(c) || c == ':' || c == '-';

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
                var image = ParseImage(source);
                if (image == null || tag.Closing) return false;
                image.Bold = style.Bold;
                image.Italic = style.Italic;
                image.Code = style.Code;
                image.InlineCode = style.InlineCode;
                image.Strike = style.Strike;
                image.Link = style.Link;
                image.LocalLink = style.LocalLink;
                target.Add(image);
                return true;
            }

            if (name == "br")
            {
                if (!tag.Closing)
                    AddRun(target, "\n", style);
                return true;
            }

            if (name == "strong" || name == "b")
            {
                state.Bold = Math.Max(0, state.Bold + (tag.Closing ? -1 : 1));
                return true;
            }
            if (name == "em" || name == "i")
            {
                state.Italic = Math.Max(0, state.Italic + (tag.Closing ? -1 : 1));
                return true;
            }
            if (name == "code" || name == "kbd" || name == "samp")
            {
                state.Code = Math.Max(0, state.Code + (tag.Closing ? -1 : 1));
                state.InlineCode = Math.Max(0, state.InlineCode + (tag.Closing ? -1 : 1));
                return true;
            }
            if (name == "del" || name == "s" || name == "strike")
            {
                state.Strike = Math.Max(0, state.Strike + (tag.Closing ? -1 : 1));
                return true;
            }
            if (name == "a")
            {
                if (tag.Closing)
                {
                    state.Link = null;
                    state.LocalLink = null;
                }
                else
                {
                    _paths.TryResolveLink(HtmlAttribute(tag.Attributes, "href"),
                        out state.Link, out state.LocalLink);
                }
                return true;
            }

            // Span is intentionally style-free. It is common in generated Markdown and
            // stripping only this structural tag is safe. CSS is deliberately not interpreted.
            if (name == "span") return true;

            return false;
        }

        void AppendImage(LinkInline image, List<InlineRun> target, InlineStyle style)
        {
            if (image == null || string.IsNullOrWhiteSpace(image.Url)) return;
            target.Add(new InlineRun
            {
                IsImage = true,
                ImagePath = MarkdownMarkup.Decode(image.Url),
                ImageAlt = MarkdownMarkup.Decode(InlineText(image)),
                Bold = style.Bold,
                Italic = style.Italic,
                Code = style.Code,
                InlineCode = style.InlineCode,
                Strike = style.Strike,
                Link = style.Link,
                LocalLink = style.LocalLink,
            });
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
            if (inline is CodeInline code)
            {
                target.Append(code.Content);
                return;
            }
            if (inline is LineBreakInline)
            {
                target.Append('\n');
                return;
            }
            if (inline is ContainerInline container)
                foreach (Inline child in container)
                    AppendInlineText(child, target);
        }

        static InlineRun ParseImage(string html)
        {
            if (!TryParseHtmlTag(html, out var tag) || tag.Comment || tag.Closing ||
                tag.Name != "img") return null;

            string source = HtmlAttribute(tag.Attributes, "src");
            if (string.IsNullOrWhiteSpace(source)) return null;

            return new InlineRun
            {
                IsImage = true,
                ImagePath = MarkdownMarkup.Decode(source),
                ImageAlt = MarkdownMarkup.Decode(HtmlAttribute(tag.Attributes, "alt") ?? ""),
                ImageWidth = HtmlDimension(HtmlAttribute(tag.Attributes, "width")),
                ImageHeight = HtmlDimension(HtmlAttribute(tag.Attributes, "height")),
                ImageAlign = ImageAlignment(tag.Attributes),
            };
        }

        static string ImageAlignment(string attrs)
        {
            string align = (HtmlAttribute(attrs, "align") ?? "").Trim().ToLowerInvariant();
            if (align.Length == 0)
                align = (CssProperty(HtmlAttribute(attrs, "style"), "float") ?? "")
                    .Trim().ToLowerInvariant();
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

        static string HtmlAttribute(string attrs, string name)
        {
            attrs = attrs ?? "";
            for (int at = 0; at < attrs.Length;)
            {
                while (at < attrs.Length && (char.IsWhiteSpace(attrs[at]) || attrs[at] == '/')) at++;
                int keyStart = at;
                while (at < attrs.Length && !char.IsWhiteSpace(attrs[at]) &&
                    attrs[at] != '=' && attrs[at] != '>') at++;
                if (at == keyStart)
                {
                    at++;
                    continue;
                }

                string key = attrs.Substring(keyStart, at - keyStart);
                while (at < attrs.Length && char.IsWhiteSpace(attrs[at])) at++;
                if (at >= attrs.Length || attrs[at] != '=') continue;
                at++;
                while (at < attrs.Length && char.IsWhiteSpace(attrs[at])) at++;
                if (at >= attrs.Length) return null;

                char quote = attrs[at] == '\"' || attrs[at] == '\'' ? attrs[at++] : '\0';
                int valueStart = at;
                if (quote != '\0')
                {
                    while (at < attrs.Length && attrs[at] != quote) at++;
                }
                else
                {
                    while (at < attrs.Length && !char.IsWhiteSpace(attrs[at]) && attrs[at] != '>') at++;
                }
                string value = attrs.Substring(valueStart, at - valueStart);
                if (quote != '\0' && at < attrs.Length) at++;
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return value;
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
            target.Add(new InlineRun
            {
                Text = text,
                Bold = style.Bold,
                Italic = style.Italic,
                Code = style.Code,
                InlineCode = style.InlineCode,
                Strike = style.Strike,
                Faint = faint,
                Link = style.Link,
                LocalLink = style.LocalLink,
            });
        }
    }
}
