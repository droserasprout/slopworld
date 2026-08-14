using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A deliberately native Markdown reader. Markdig supplies the CommonMark/GFM parse tree;
    // this class supplies the small, scheme-coloured document layout that belongs in the game.
    // It never turns Markdown into HTML: the small native renderer handles safe image tags
    // itself, while other HTML remains literal, faint text. Links are checked again before
    // they reach the daemon's external opener.
    public sealed class MarkdownPreview : IContentView
    {
        enum BlockKind { Paragraph, Heading, Code, Quote, List, Item, Rule, Table, Raw }
        enum PlacementKind { Text, Image, Code, Rule, Quote, Table, Bullet }

        sealed class InlineRun
        {
            public string Text;
            public bool Bold;
            public bool Italic;
            public bool Code;
            public bool Faint;
            public string Link;
            public string ImagePath;
            public float ImageWidth;
            public float ImageHeight;
            public string ImageAlign;
        }

        sealed class MarkdownBlock
        {
            public BlockKind Kind;
            public int Level;
            public bool Ordered;
            public int Start;
            public string Code;
            public string Info;
            public List<InlineRun> Runs;
            public List<MarkdownBlock> Children;
            public List<TableRow> Rows;
        }

        sealed class TableRow
        {
            public bool Header;
            public List<List<InlineRun>> Cells = new List<List<InlineRun>>();
        }

        sealed class TextPiece
        {
            public string Text;
            public InlineRun Run;
            public GUIStyle Style;
            public float Width;
            public float Height;
        }

        sealed class TextLine
        {
            public readonly List<TextPiece> Pieces = new List<TextPiece>();
            public float Width;
            public float Height;
        }

        sealed class TextLayout
        {
            public readonly List<TextLine> Lines = new List<TextLine>();
            public float Height;
        }

        sealed class TableLayout
        {
            public readonly List<TableRowLayout> Rows = new List<TableRowLayout>();
            public float[] Widths;
            public float Height;
        }

        sealed class TableRowLayout
        {
            public bool Header;
            public readonly List<TextLayout> Cells = new List<TextLayout>();
            public float Height;
        }

        sealed class Placement
        {
            public PlacementKind Kind;
            public float X;
            public float Y;
            public float Width;
            public float Height;
            public TextLayout Text;
            public TableLayout Table;
            public string Label;
            public bool Heading;
            public InlineRun Image;
        }

        sealed class SelectionLine
        {
            public float X;
            public float Y;
            public float Height;
            public string Text;
            public readonly List<float> Edges = new List<float>();
        }

        struct LinkHit
        {
            public Rect Rect;
            public string Url;

            public LinkHit(Rect rect, string url)
            {
                Rect = rect;
                Url = url;
            }
        }

        sealed class StyleSet
        {
            public GUIStyle Normal;
            public GUIStyle Bold;
            public GUIStyle Italic;
            public GUIStyle BoldItalic;
            public GUIStyle Code;
            public GUIStyle H1;
            public GUIStyle H2;
            public GUIStyle H3;
            public GUIStyle H4;

            public StyleSet()
            {
                var oldFont = Text.Font;
                try
                {
                    Text.Font = GameFont.Small;
                    Normal = Make(Text.CurFontStyle, FontStyle.Normal, 0);
                    Bold = Make(Text.CurFontStyle, FontStyle.Bold, 0);
                    Italic = Make(Text.CurFontStyle, FontStyle.Italic, 0);
                    BoldItalic = Make(Text.CurFontStyle, FontStyle.BoldAndItalic, 0);

                    Text.Font = GameFont.Medium;
                    H1 = Make(Text.CurFontStyle, FontStyle.Bold, 2);
                    H2 = Make(Text.CurFontStyle, FontStyle.Bold, 1);
                    H3 = Make(Text.CurFontStyle, FontStyle.Bold, 0);
                    H4 = Make(Text.CurFontStyle, FontStyle.Normal, 0);

                    Code = new GUIStyle(TerminalFont.Style)
                    {
                        alignment = TextAnchor.UpperLeft,
                        clipping = TextClipping.Overflow,
                        margin = new RectOffset(0, 0, 0, 0),
                        padding = new RectOffset(0, 0, 0, 0),
                        richText = false,
                        wordWrap = false,
                    };
                }
                finally
                {
                    Text.Font = oldFont;
                }
            }

            static GUIStyle Make(GUIStyle source, FontStyle fontStyle, int delta)
            {
                var style = new GUIStyle(source)
                {
                    alignment = TextAnchor.UpperLeft,
                    clipping = TextClipping.Overflow,
                    fontStyle = fontStyle,
                    margin = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(0, 0, 0, 0),
                    richText = false,
                    wordWrap = false,
                };
                if (style.fontSize > 0) style.fontSize += delta;
                return style;
            }

            public GUIStyle For(InlineRun run, int heading)
            {
                if (run.Code) return Code;
                if (heading == 1) return H1;
                if (heading == 2) return H2;
                if (heading == 3) return H3;
                if (heading == 4) return H4;
                if (run.Bold && run.Italic) return BoldItalic;
                if (run.Bold) return Bold;
                if (run.Italic) return Italic;
                return Normal;
            }
        }

        static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseGridTables()
            .UseTaskLists()
            .UseAutoLinks()
            .Build();

        static readonly Regex ImageTag = new Regex(
            @"<img\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase);

        readonly string _path;
        readonly string _name;
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly List<Placement> _placements = new List<Placement>();
        readonly List<LinkHit> _links = new List<LinkHit>();
        readonly List<SelectionLine> _selectionLines = new List<SelectionLine>();
        readonly Dictionary<string, Texture2D> _images = new Dictionary<string, Texture2D>();
        readonly HashSet<string> _pendingImages = new HashSet<string>();
        readonly HashSet<string> _failedImages = new HashSet<string>();

        bool _dragging;
        bool _wordDragging;
        bool _hasSel;
        Vector2Int _wordStart;
        Vector2Int _wordEnd;
        Vector2Int _selA;
        Vector2Int _selB;

        List<MarkdownBlock> _blocks;
        StyleSet _styles;
        string _error;
        bool _loading;
        int _request;
        float _width = -1f;
        float _height;

        public MarkdownPreview(string project, string path, string name)
        {
            _path = path;
            _name = string.IsNullOrEmpty(name) ? System.IO.Path.GetFileName(path) : name;
        }

        public string Path => _path;
        public string Title => "Preview · " + _name;

        public static void Open(string project, string path, string name) =>
            TerminalWindow.OpenContent(new MarkdownPreview(project, path, name));

        public static bool IsShowing(string path) =>
            TerminalWindow.ShowingAs<MarkdownPreview>()?.Path == path;

        public static void CloseIfShowing()
        {
            if (TerminalWindow.ShowingAs<MarkdownPreview>() == null) return;
            Find.WindowStack?.WindowOfType<TerminalWindow>()?.Leave();
        }

        public void Opened()
        {
            _loading = true;
            _error = null;
            _blocks = null;
            _width = -1f;
            int request = ++_request;
            SlopClient.Get("/api/read?path=" + Uri.EscapeDataString(_path),
                j =>
                {
                    if (request != _request) return;
                    try
                    {
                        _blocks = Parse(j["text"].AsString());
                        RequestImages(_blocks, request);
                        _loading = false;
                        _error = null;
                        _scroll.JumpTo(Vector2.zero);
                    }
                    catch (Exception e)
                    {
                        _loading = false;
                        _error = "Markdown could not be parsed: " + e.Message;
                    }
                },
                msg =>
                {
                    if (request != _request) return;
                    _loading = false;
                    _error = msg;
                });
        }

        public void Closed()
        {
            ++_request;
            _scroll.JumpTo(Vector2.zero);
            ClearSelection();
            foreach (var texture in _images.Values)
                if (texture != null) UnityEngine.Object.Destroy(texture);
            _images.Clear();
            _pendingImages.Clear();
            _failedImages.Clear();
        }

        public void Draw(Rect body)
        {
            if (_loading || _blocks == null)
            {
                Status(body, _error ?? "Loading Markdown…", _error == null
                    ? SlopWidgets.Dim : SlopWidgets.Bad);
                return;
            }

            if (_styles == null) _styles = new StyleSet();

            Reflow(body.width);
            float width = body.width;
            if (_height > body.height)
            {
                width = Mathf.Max(1f, body.width - SlopWidgets.ScrollbarW);
                if (!Mathf.Approximately(width, _width)) Reflow(width);
            }

            var view = new Rect(0f, 0f, width, Mathf.Max(body.height, _height));
            _links.Clear();
            CollectSelection();
            _scroll.Begin(body, view);
            try
            {
                foreach (var placement in _placements)
                    DrawPlacementBackground(placement);
                DrawSelectionHighlights();
                foreach (var placement in _placements)
                    DrawPlacementForeground(placement);
            }
            finally
            {
                _scroll.End();
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }

            ClickLinks(body);
            HandleInput(body);
        }

        static void Status(Rect body, string text, Color color)
        {
            var old = GUI.color;
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = color;
                Widgets.Label(body, text);
            }
            finally
            {
                GUI.color = old;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
            }
        }

        void Reflow(float width)
        {
            width = Mathf.Max(1f, width);
            if (Mathf.Approximately(width, _width)) return;

            _width = width;
            _placements.Clear();
            float y = SlopWidgets.GapM;
            foreach (var block in _blocks)
                y = Place(block, SlopWidgets.GapM, y, width);
            _height = Mathf.Max(1f, y + SlopWidgets.GapM);
        }

        void RequestImages(List<MarkdownBlock> blocks, int request)
        {
            foreach (var block in blocks ?? new List<MarkdownBlock>())
                RequestImages(block, request);
        }

        void RequestImages(MarkdownBlock block, int request)
        {
            if (block == null) return;
            RequestImages(block.Runs, request);
            if (block.Rows != null)
                foreach (var row in block.Rows)
                    foreach (var cell in row.Cells)
                        RequestImages(cell, request);
            if (block.Children != null)
                foreach (var child in block.Children)
                    RequestImages(child, request);
        }

        void RequestImages(List<InlineRun> runs, int request)
        {
            foreach (var run in runs ?? new List<InlineRun>())
            {
                if (string.IsNullOrWhiteSpace(run.ImagePath)) continue;
                string path = ResolveImagePath(run.ImagePath);
                if (path == null)
                {
                    run.ImagePath = null;
                    continue;
                }
                run.ImagePath = path;
                if (_images.ContainsKey(path) || _pendingImages.Contains(path) ||
                    _failedImages.Contains(path)) continue;

                _pendingImages.Add(path);
                SlopClient.Send("GET", "/api/image?path=" + Uri.EscapeDataString(path), null,
                    j =>
                    {
                        if (request != _request) return;
                        _pendingImages.Remove(path);
                        try
                        {
                            var bytes = Convert.FromBase64String(j["data"].AsString());
                            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                            if (!texture.LoadImage(bytes, true))
                                throw new InvalidDataException("Unity could not decode the image");
                            texture.name = "SlopWorld Markdown " +
                                System.IO.Path.GetFileName(path);
                            texture.hideFlags = HideFlags.HideAndDontSave;
                            _images[path] = texture;
                            _width = -1f;
                        }
                        catch
                        {
                            _failedImages.Add(path);
                        }
                    },
                    msg =>
                    {
                        if (request != _request) return;
                        _pendingImages.Remove(path);
                        _failedImages.Add(path);
                    });
            }
        }

        string ResolveImagePath(string source)
        {
            source = HtmlDecode(source).Trim();
            if (source.Length == 0 || source.StartsWith("//", StringComparison.Ordinal) ||
                source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                !string.IsNullOrEmpty(uri.Scheme)) return null;

            string baseDir = System.IO.Path.GetDirectoryName(_path) ?? ".";
            string local = source.Replace('/', System.IO.Path.DirectorySeparatorChar);
            return System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(local)
                ? local : System.IO.Path.Combine(baseDir, local));
        }

        Texture2D ImageFor(InlineRun image)
        {
            if (image == null || string.IsNullOrEmpty(image.ImagePath)) return null;
            _images.TryGetValue(image.ImagePath, out var texture);
            return texture;
        }

        void ImageMetrics(InlineRun image, float available, out float width, out float height)
        {
            var texture = ImageFor(image);
            float naturalWidth = texture == null ? 320f : texture.width;
            float naturalHeight = texture == null ? 180f : texture.height;

            width = image.ImageWidth > 0f ? image.ImageWidth : naturalWidth;
            height = image.ImageHeight > 0f ? image.ImageHeight : naturalHeight;
            if (image.ImageWidth > 0f && image.ImageHeight <= 0f && texture != null)
                height = width * texture.height / Mathf.Max(1f, texture.width);
            else if (image.ImageHeight > 0f && image.ImageWidth <= 0f && texture != null)
                width = height * texture.width / Mathf.Max(1f, texture.height);

            width = Mathf.Clamp(width, 1f, Mathf.Max(1f, available));
            if (image.ImageHeight <= 0f && texture == null)
                height = width * naturalHeight / naturalWidth;
            height = Mathf.Max(1f, height);
        }

        float Place(MarkdownBlock block, float x, float y, float width)
        {
            switch (block.Kind)
            {
                case BlockKind.Paragraph:
                    if (TrySingleImage(block.Runs, out var image))
                        return PlaceImage(image, x, y, width, SlopWidgets.GapS);
                    if (TryAlignedImage(block.Runs, out image))
                        return PlaceParagraphWithImage(block.Runs, image, x, y, width);
                    return PlaceText(block.Runs, x, y, width, 0, false, SlopWidgets.GapS);

                case BlockKind.Heading:
                    return PlaceText(block.Runs, x, y, width, block.Level, true, SlopWidgets.GapM);

                case BlockKind.Code:
                    return PlaceCode(block, x, y, width);

                case BlockKind.Raw:
                    return PlaceText(block.Runs, x, y, width, 0, false, SlopWidgets.GapS);

                case BlockKind.Rule:
                    _placements.Add(new Placement
                    {
                        Kind = PlacementKind.Rule,
                        X = x,
                        Y = y + SlopWidgets.GapS,
                        Width = width,
                        Height = 1f,
                    });
                    return y + SlopWidgets.GapS * 2f + 1f;

                case BlockKind.Quote:
                    return PlaceQuote(block, x, y, width);

                case BlockKind.List:
                    return PlaceList(block, x, y, width);

                case BlockKind.Table:
                    return PlaceTable(block, x, y, width);

                case BlockKind.Item:
                    foreach (var child in block.Children)
                        y = Place(child, x, y, width);
                    return y;

                default:
                    return y;
            }
        }

        float PlaceParagraphWithImage(List<InlineRun> runs, InlineRun image,
                                      float x, float y, float width)
        {
            ImageMetrics(image, width, out float imageWidth, out float imageHeight);
            var textRuns = new List<InlineRun>();
            foreach (var run in runs)
                if (run != image) textRuns.Add(run);

            float textWidth = Mathf.Max(1f, width - imageWidth - SlopWidgets.GapS);
            var text = Wrap(textRuns, textWidth, 0);
            _placements.Add(new Placement
            {
                Kind = PlacementKind.Text,
                X = x,
                Y = y,
                Width = textWidth,
                Height = text.Height,
                Text = text,
            });
            _placements.Add(new Placement
            {
                Kind = PlacementKind.Image,
                X = x + width - imageWidth,
                Y = y,
                Width = imageWidth,
                Height = imageHeight,
                Image = image,
            });
            return y + Mathf.Max(text.Height, imageHeight) + SlopWidgets.GapS;
        }

        float PlaceImage(InlineRun image, float x, float y, float width, float gap)
        {
            ImageMetrics(image, width, out float imageWidth, out float imageHeight);
            float imageX = image.ImageAlign == "right"
                ? x + width - imageWidth
                : image.ImageAlign == "center"
                    ? x + (width - imageWidth) / 2f
                    : x;
            _placements.Add(new Placement
            {
                Kind = PlacementKind.Image,
                X = imageX,
                Y = y,
                Width = imageWidth,
                Height = imageHeight,
                Image = image,
            });
            return y + imageHeight + gap;
        }

        static bool TrySingleImage(List<InlineRun> runs, out InlineRun image)
        {
            image = null;
            foreach (var run in runs ?? new List<InlineRun>())
            {
                if (!string.IsNullOrWhiteSpace(run.ImagePath))
                {
                    if (image != null) return false;
                    image = run;
                }
                else if (!string.IsNullOrWhiteSpace(run.Text)) return false;
            }
            return image != null;
        }

        static bool TryAlignedImage(List<InlineRun> runs, out InlineRun image)
        {
            image = null;
            foreach (var run in runs ?? new List<InlineRun>())
            {
                if (string.IsNullOrWhiteSpace(run.ImagePath)) continue;
                if (image != null || run.ImageAlign != "right") return false;
                image = run;
            }
            return image != null;
        }

        float PlaceText(List<InlineRun> runs, float x, float y, float width,
                        int heading, bool headingColor, float gap)
        {
            var text = Wrap(runs, width, heading);
            _placements.Add(new Placement
            {
                Kind = PlacementKind.Text,
                X = x,
                Y = y,
                Width = width,
                Height = text.Height,
                Text = text,
                Heading = headingColor,
            });
            return y + text.Height + gap;
        }

        float PlaceCode(MarkdownBlock block, float x, float y, float width)
        {
            var runs = new List<InlineRun>
            {
                new InlineRun { Text = block.Code ?? "", Code = true },
            };
            var text = Wrap(runs, Mathf.Max(1f, width - SlopWidgets.GapS * 2f), 0);
            float labelHeight = string.IsNullOrWhiteSpace(block.Info)
                ? 0f : SlopWidgets.TinyH + SlopWidgets.GapXS;
            float h = text.Height + SlopWidgets.GapS * 2f + labelHeight;
            _placements.Add(new Placement
            {
                Kind = PlacementKind.Code,
                X = x,
                Y = y,
                Width = width,
                Height = h,
                Text = text,
                Label = block.Info,
            });
            return y + h + SlopWidgets.GapS;
        }

        float PlaceQuote(MarkdownBlock block, float x, float y, float width)
        {
            float start = y;
            float innerX = x + SlopWidgets.GapM;
            float innerWidth = Mathf.Max(1f, width - SlopWidgets.GapM);
            foreach (var child in block.Children)
                y = Place(child, innerX, y, innerWidth);

            _placements.Add(new Placement
            {
                Kind = PlacementKind.Quote,
                X = x,
                Y = start,
                Width = width,
                Height = Mathf.Max(SlopWidgets.LineH, y - start - SlopWidgets.GapS),
            });
            return y + SlopWidgets.GapS;
        }

        float PlaceList(MarkdownBlock block, float x, float y, float width)
        {
            int number = block.Start;
            foreach (var item in block.Children)
            {
                string marker = block.Ordered ? number++ + "." : "•";
                var bullet = new List<InlineRun>
                {
                    new InlineRun { Text = marker },
                };
                var bulletText = Wrap(bullet, width, 0);
                _placements.Add(new Placement
                {
                    Kind = PlacementKind.Bullet,
                    X = x,
                    Y = y,
                    Width = SlopWidgets.GapL,
                    Height = bulletText.Height,
                    Text = bulletText,
                });

                float itemY = y;
                float innerX = x + SlopWidgets.GapL;
                float innerWidth = Mathf.Max(1f, width - SlopWidgets.GapL);
                foreach (var child in item.Children)
                    itemY = Place(child, innerX, itemY, innerWidth);
                y = Mathf.Max(itemY, y + bulletText.Height) + SlopWidgets.GapXS;
            }
            return y + SlopWidgets.GapXS;
        }

        float PlaceTable(MarkdownBlock block, float x, float y, float width)
        {
            var table = MakeTable(block, width);
            _placements.Add(new Placement
            {
                Kind = PlacementKind.Table,
                X = x,
                Y = y,
                Width = width,
                Height = table.Height,
                Table = table,
            });
            return y + table.Height + SlopWidgets.GapM;
        }

        TextLayout Wrap(List<InlineRun> runs, float width, int heading)
        {
            var layout = new TextLayout();
            var line = NewLine(_styles.Normal);

            foreach (var run in runs ?? new List<InlineRun>())
            {
                if (!string.IsNullOrEmpty(run.ImagePath))
                {
                    ImageMetrics(run, width, out float imageWidth, out float imageHeight);
                    if (line.Pieces.Count > 0 && line.Width + imageWidth > width)
                    {
                        layout.Lines.Add(line);
                        line = NewLine(_styles.Normal);
                    }
                    AddPiece(line, run, "", _styles.Normal, imageWidth, imageHeight);
                    continue;
                }

                string text = run.Text ?? "";
                GUIStyle style = _styles.For(run, heading);
                int start = 0;
                while (start <= text.Length)
                {
                    int newline = text.IndexOf('\n', start);
                    int end = newline < 0 ? text.Length : newline;
                    AppendWrapped(ref line, layout, run, text.Substring(start, end - start), style, width);
                    if (newline < 0) break;
                    layout.Lines.Add(line);
                    line = NewLine(style);
                    start = newline + 1;
                }
            }

            if (line.Pieces.Count > 0 || layout.Lines.Count == 0)
                layout.Lines.Add(line);
            layout.Height = 0f;
            foreach (var item in layout.Lines)
            {
                if (item.Height <= 0f) item.Height = SlopWidgets.LineH;
                layout.Height += item.Height;
            }
            return layout;
        }

        TextLine NewLine(GUIStyle style) => new TextLine
        {
            Height = Mathf.Max(SlopWidgets.LineH, style?.lineHeight ?? SlopWidgets.LineH),
        };

        void AppendWrapped(ref TextLine line, TextLayout layout, InlineRun run, string text,
                           GUIStyle style, float width)
        {
            int start = 0;
            while (start < text.Length)
            {
                bool space = char.IsWhiteSpace(text[start]);
                int end = start + 1;
                while (end < text.Length && char.IsWhiteSpace(text[end]) == space) end++;
                string chunk = text.Substring(start, end - start);
                float chunkWidth = Measure(style, chunk);

                if (space)
                {
                    if (line.Pieces.Count > 0 && line.Width + chunkWidth <= width)
                        AddPiece(line, run, chunk, style, chunkWidth);
                }
                else if (line.Pieces.Count > 0 && line.Width + chunkWidth > width)
                {
                    layout.Lines.Add(line);
                    line = NewLine(style);
                    AddWord(ref line, layout, run, chunk, style, width);
                }
                else
                {
                    AddWord(ref line, layout, run, chunk, style, width);
                }

                start = end;
            }
        }

        void AddWord(ref TextLine line, TextLayout layout, InlineRun run, string word,
                     GUIStyle style, float width)
        {
            float wordWidth = Measure(style, word);
            if (line.Pieces.Count == 0 && wordWidth <= width)
            {
                AddPiece(line, run, word, style, wordWidth);
                return;
            }

            if (wordWidth <= width)
            {
                AddPiece(line, run, word, style, wordWidth);
                return;
            }

            for (int i = 0; i < word.Length; i++)
            {
                string character = word[i].ToString();
                float charWidth = Measure(style, character);
                if (line.Pieces.Count > 0 && line.Width + charWidth > width)
                {
                    layout.Lines.Add(line);
                    line = NewLine(style);
                }
                AddPiece(line, run, character, style, charWidth);
            }
        }

        static float Measure(GUIStyle style, string text) =>
            style.CalcSize(new GUIContent(text ?? "")).x;

        static void AddPiece(TextLine line, InlineRun run, string text,
                             GUIStyle style, float width, float height = 0f)
        {
            if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(run.ImagePath)) return;
            line.Pieces.Add(new TextPiece
            {
                Text = text,
                Run = run,
                Style = style,
                Width = width,
                Height = height,
            });
            line.Width += width;
            line.Height = Mathf.Max(line.Height, height > 0f ? height : style.lineHeight);
        }

        TableLayout MakeTable(MarkdownBlock block, float width)
        {
            int columns = 0;
            foreach (var row in block.Rows) columns = Mathf.Max(columns, row.Cells.Count);
            columns = Mathf.Max(1, columns);

            var table = new TableLayout { Widths = new float[columns] };
            float cellWidth = width / columns;
            foreach (var row in block.Rows)
            {
                var result = new TableRowLayout { Header = row.Header };
                foreach (var cell in row.Cells)
                {
                    var text = Wrap(cell, Mathf.Max(1f, cellWidth - SlopWidgets.GapS * 2f), 0);
                    result.Cells.Add(text);
                    result.Height = Mathf.Max(result.Height, text.Height + SlopWidgets.GapS * 2f);
                }
                while (result.Cells.Count < columns)
                    result.Cells.Add(Wrap(new List<InlineRun>(),
                        Mathf.Max(1f, cellWidth - SlopWidgets.GapS * 2f), 0));
                table.Rows.Add(result);
                table.Height += result.Height;
            }
            for (int i = 0; i < columns; i++) table.Widths[i] = cellWidth;
            return table;
        }

        void DrawPlacementBackground(Placement placement)
        {
            switch (placement.Kind)
            {
                case PlacementKind.Text:
                case PlacementKind.Bullet:
                    DrawInlineCodeBackgrounds(placement.Text, placement.X, placement.Y);
                    break;

                case PlacementKind.Code:
                    Slab.Box(new Rect(placement.X, placement.Y, placement.Width, placement.Height),
                        SlopWidgets.Well, SlopWidgets.Edge);
                    DrawInlineCodeBackgrounds(placement.Text, placement.X + SlopWidgets.GapS,
                        placement.Y + SlopWidgets.GapS +
                        (string.IsNullOrWhiteSpace(placement.Label)
                            ? 0f : SlopWidgets.TinyH + SlopWidgets.GapXS));
                    break;

                case PlacementKind.Rule:
                    Slab.Hairline(new Rect(placement.X, placement.Y,
                        placement.Width, placement.Height), SlopWidgets.Edge);
                    break;

                case PlacementKind.Quote:
                    Slab.Fill(new Rect(placement.X, placement.Y, 3f, placement.Height),
                        SlopWidgets.Accent);
                    break;

                case PlacementKind.Table:
                    DrawTableBackground(placement);
                    DrawTableInlineCodeBackgrounds(placement);
                    break;
            }
        }

        void DrawInlineCodeBackgrounds(TextLayout text, float x, float y)
        {
            if (text == null) return;
            foreach (var line in text.Lines)
            {
                float at = x;
                foreach (var piece in line.Pieces)
                {
                    if (piece.Run.Code)
                        Slab.Fill(new Rect(at, y, piece.Width, line.Height).ContractedBy(1f),
                            SlopWidgets.RowBg);
                    at += piece.Width;
                }
                y += line.Height;
            }
        }

        void DrawTableInlineCodeBackgrounds(Placement placement)
        {
            float y = placement.Y;
            foreach (var row in placement.Table.Rows)
            {
                float x = placement.X;
                for (int i = 0; i < row.Cells.Count; i++)
                {
                    DrawInlineCodeBackgrounds(row.Cells[i], x + SlopWidgets.GapS,
                        y + SlopWidgets.GapS);
                    x += placement.Table.Widths[i];
                }
                y += row.Height;
            }
        }

        void DrawPlacementForeground(Placement placement)
        {
            switch (placement.Kind)
            {
                case PlacementKind.Text:
                case PlacementKind.Bullet:
                    DrawText(placement.Text, placement.X, placement.Y, placement.Heading);
                    break;

                case PlacementKind.Image:
                    DrawImage(placement);
                    break;

                case PlacementKind.Code:
                    if (!string.IsNullOrEmpty(placement.Label))
                    {
                        var label = placement.Label.Trim();
                        if (label.Length > 0)
                        {
                            GUI.color = SlopWidgets.Dim;
                            Text.Font = GameFont.Tiny;
                            Widgets.Label(new Rect(placement.X + SlopWidgets.GapS,
                                placement.Y + SlopWidgets.GapS, placement.Width, SlopWidgets.TinyH),
                                label);
                            GUI.color = Color.white;
                        }
                    }
                    DrawText(placement.Text, placement.X + SlopWidgets.GapS,
                        placement.Y + SlopWidgets.GapS +
                        (string.IsNullOrWhiteSpace(placement.Label)
                            ? 0f : SlopWidgets.TinyH + SlopWidgets.GapXS), false);
                    break;

                case PlacementKind.Table:
                    DrawTableText(placement);
                    break;
            }
        }

        void CollectSelection()
        {
            _selectionLines.Clear();
            foreach (var placement in _placements)
            {
                switch (placement.Kind)
                {
                    case PlacementKind.Text:
                    case PlacementKind.Bullet:
                        CollectText(placement.Text, placement.X, placement.Y);
                        break;

                    case PlacementKind.Code:
                        CollectText(placement.Text, placement.X + SlopWidgets.GapS,
                            placement.Y + SlopWidgets.GapS +
                            (string.IsNullOrWhiteSpace(placement.Label)
                                ? 0f : SlopWidgets.TinyH + SlopWidgets.GapXS));
                        break;

                    case PlacementKind.Table:
                        CollectTableSelection(placement);
                        break;
                }
            }
        }

        void CollectText(TextLayout text, float x, float y)
        {
            if (text == null) return;

            foreach (var line in text.Lines)
            {
                CollectTextLine(line, x, y);
                y += line.Height;
            }
        }

        void CollectTextLine(TextLine line, float x, float y)
        {
            var output = new SelectionLine
            {
                X = x,
                Y = y,
                Height = line.Height,
                Text = "",
            };
            output.Edges.Add(0f);
            float at = x;
            var chars = new System.Text.StringBuilder();
            foreach (var piece in line.Pieces)
            {
                if (!string.IsNullOrEmpty(piece.Run.ImagePath))
                {
                    at += piece.Width;
                    continue;
                }
                for (int i = 0; i < piece.Text.Length; i++)
                {
                    chars.Append(piece.Text[i]);
                    at += Measure(piece.Style, piece.Text[i].ToString());
                    output.Edges.Add(at - x);
                }
            }
            output.Text = chars.ToString();
            _selectionLines.Add(output);
        }

        void CollectTableSelection(Placement placement)
        {
            float y = placement.Y;
            foreach (var row in placement.Table.Rows)
            {
                int lines = 0;
                foreach (var cell in row.Cells) lines = Mathf.Max(lines, cell.Lines.Count);
                for (int lineIndex = 0; lineIndex < lines; lineIndex++)
                {
                    float x = placement.X;
                    for (int i = 0; i < row.Cells.Count; i++)
                    {
                        var cell = row.Cells[i];
                        if (lineIndex < cell.Lines.Count)
                        {
                            float lineY = y + SlopWidgets.GapS;
                            for (int j = 0; j < lineIndex; j++)
                                lineY += cell.Lines[j].Height;
                            CollectTextLine(cell.Lines[lineIndex], x + SlopWidgets.GapS, lineY);
                        }
                        x += placement.Table.Widths[i];
                    }
                }
                y += row.Height;
            }
        }

        void DrawSelectionHighlights()
        {
            if (!_hasSel || _selectionLines.Count == 0) return;
            OrderedSelection(out var a, out var b);

            int first = Mathf.Clamp(a.y, 0, _selectionLines.Count - 1);
            int last = Mathf.Clamp(b.y, 0, _selectionLines.Count - 1);
            for (int i = first; i <= last; i++)
            {
                var line = _selectionLines[i];
                int start = i == a.y ? a.x : 0;
                int end = i == b.y ? b.x : line.Text.Length;
                start = Mathf.Clamp(start, 0, line.Text.Length);
                end = Mathf.Clamp(end, start, line.Text.Length);
                if (end <= start) continue;

                float left = line.X + line.Edges[start];
                float right = line.X + line.Edges[end];
                Slab.Fill(new Rect(left, line.Y, right - left, line.Height), SlopWidgets.Sel);
            }
        }

        void DrawText(TextLayout text, float x, float y, bool heading)
        {
            foreach (var line in text.Lines)
            {
                float at = x;
                foreach (var piece in line.Pieces)
                {
                    var rect = new Rect(at, y, piece.Width, line.Height);
                    if (!string.IsNullOrEmpty(piece.Run.ImagePath))
                    {
                        var texture = ImageFor(piece.Run);
                        if (texture != null)
                        {
                            GUI.color = Color.white;
                            GUI.DrawTexture(new Rect(at, y, piece.Width, piece.Height), texture,
                                ScaleMode.ScaleToFit, true);
                        }
                        else
                        {
                            Slab.Box(new Rect(at, y, piece.Width, piece.Height),
                                SlopWidgets.Well, SlopWidgets.Edge);
                            GUI.color = SlopWidgets.Dim;
                            Text.Font = GameFont.Tiny;
                            Widgets.Label(new Rect(at + SlopWidgets.GapXS, y,
                                Mathf.Max(1f, piece.Width - SlopWidgets.GapXS * 2f), piece.Height),
                                "image loading…");
                            GUI.color = Color.white;
                        }
                        at += piece.Width;
                        continue;
                    }

                    var old = GUI.color;
                    GUI.color = piece.Run.Link != null ? SlopWidgets.Accent
                        : piece.Run.Faint ? SlopWidgets.Dim
                        : piece.Run.Code ? SlopWidgets.Lead
                        : heading ? SlopWidgets.Lead : SlopWidgets.Name;
                    GUI.Label(rect, piece.Text, piece.Style);
                    if (piece.Run.Link != null)
                    {
                        Slab.Hairline(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                            SlopWidgets.Accent);
                        _links.Add(new LinkHit(rect, piece.Run.Link));
                    }
                    GUI.color = old;
                    at += piece.Width;
                }
                y += line.Height;
            }
        }

        void DrawImage(Placement placement)
        {
            var texture = ImageFor(placement.Image);
            var rect = new Rect(placement.X, placement.Y, placement.Width, placement.Height);
            if (texture != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
                return;
            }

            Slab.Box(rect, SlopWidgets.Well, SlopWidgets.Edge);
            GUI.color = SlopWidgets.Dim;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, "image loading…");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        void DrawTableBackground(Placement placement)
        {
            float y = placement.Y;
            foreach (var row in placement.Table.Rows)
            {
                float x = placement.X;
                if (row.Header) Slab.Fill(new Rect(x, y, placement.Width, row.Height), SlopWidgets.RowBg);
                for (int i = 0; i < row.Cells.Count; i++)
                {
                    Slab.Hairline(new Rect(x, y, 1f, row.Height), SlopWidgets.Edge);
                    x += placement.Table.Widths[i];
                }
                Slab.Hairline(new Rect(placement.X, y + row.Height - 1f,
                    placement.Width, 1f), SlopWidgets.Edge);
                y += row.Height;
            }
        }

        void DrawTableText(Placement placement)
        {
            float y = placement.Y;
            foreach (var row in placement.Table.Rows)
            {
                float x = placement.X;
                for (int i = 0; i < row.Cells.Count; i++)
                {
                    DrawText(row.Cells[i], x + SlopWidgets.GapS, y + SlopWidgets.GapS,
                        row.Header);
                    x += placement.Table.Widths[i];
                }
                y += row.Height;
            }
        }

        void ClickLinks(Rect body)
        {
            var e = Event.current;
            if (e == null) return;

            foreach (var hit in _links)
            {
                var screen = new Rect(body.x + hit.Rect.x,
                    body.y + hit.Rect.y - _scroll.Position.y, hit.Rect.width, hit.Rect.height);
                if (Mouse.IsOver(screen))
                    TooltipHandler.TipRegion(screen, hit.Url + "\n\nCtrl+click to open it on the host");
                if (e.rawType == EventType.MouseDown && e.button == 0 && e.control &&
                    screen.Contains(e.mousePosition))
                {
                    SlopClient.Post("/api/open", "{\"url\":" + JVal.Q(hit.Url) + "}",
                        null, SlopWidgets.Fail);
                    e.Use();
                    return;
                }
            }
        }

        void HandleInput(Rect body)
        {
            var e = Event.current;
            if (e == null) return;

            if (e.type == EventType.KeyDown ||
                (e.type == EventType.Used && e.rawType == EventType.KeyDown))
            {
                HandleKey(e);
                return;
            }

            if (e.button == 1 && e.type == EventType.MouseDown && body.Contains(e.mousePosition))
            {
                OpenMenu();
                e.Use();
                return;
            }

            if (e.button != 0) return;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (!body.Contains(e.mousePosition)) return;
                    var point = SelectionPointAt(body, e.mousePosition);
                    if (e.clickCount >= 3)
                    {
                        SelectLine(point.y);
                        e.Use();
                        return;
                    }
                    if (e.clickCount >= 2)
                    {
                        DoubleClickSelect(point);
                        e.Use();
                        return;
                    }

                    bool extend = e.shift && _hasSel;
                    _selA = extend ? _selA : point;
                    _selB = point;
                    _dragging = true;
                    _wordDragging = false;
                    _hasSel = extend && _selA != _selB;
                    e.Use();
                    return;

                case EventType.MouseDrag:
                    if (!_dragging) return;
                    var drag = SelectionPointAt(body, e.mousePosition);
                    if (_wordDragging) UpdateWordSelection(drag);
                    else
                    {
                        _selB = drag;
                        _hasSel = _selA != _selB;
                    }
                    e.Use();
                    return;

                case EventType.MouseUp:
                    if (!_dragging) return;
                    var up = SelectionPointAt(body, e.mousePosition);
                    if (_wordDragging)
                    {
                        UpdateWordSelection(up);
                        _wordDragging = false;
                        _dragging = false;
                        if (_hasSel) CopySelection();
                    }
                    else
                    {
                        _dragging = false;
                        _selB = up;
                        if (_selA != _selB)
                        {
                            _hasSel = true;
                            CopySelection();
                        }
                        else _hasSel = false;
                    }
                    e.Use();
                    return;
            }
        }

        void HandleKey(Event e)
        {
            if (!e.control || e.alt) return;

            if (e.keyCode == KeyCode.C)
            {
                if (_hasSel) CopySelection();
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.A)
            {
                SelectAll();
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.V)
            {
                TerminalWindow.PasteClipboardToAgent();
                e.Use();
            }
        }

        Vector2Int SelectionPointAt(Rect body, Vector2 mouse)
        {
            if (_selectionLines.Count == 0) return Vector2Int.zero;

            float y = mouse.y - body.y + _scroll.Position.y;
            float x = mouse.x - body.x + _scroll.Position.x;
            int lineIndex = 0;
            float best = float.MaxValue;
            for (int i = 0; i < _selectionLines.Count; i++)
            {
                var line = _selectionLines[i];
                float vertical = y < line.Y ? line.Y - y :
                    y > line.Y + line.Height ? y - (line.Y + line.Height) : 0f;
                float left = line.X;
                float right = line.X + line.Edges[line.Text.Length];
                float horizontal = x < left ? left - x : x > right ? x - right : 0f;
                float distance = vertical * 10000f + horizontal;
                if (distance < best)
                {
                    best = distance;
                    lineIndex = i;
                }
            }

            var selected = _selectionLines[lineIndex];
            if (x <= selected.X) return new Vector2Int(0, lineIndex);
            if (x >= selected.X + selected.Edges[selected.Text.Length])
                return new Vector2Int(selected.Text.Length, lineIndex);

            for (int i = 0; i < selected.Text.Length; i++)
            {
                float left = selected.X + selected.Edges[i];
                float right = selected.X + selected.Edges[i + 1];
                if (x < (left + right) * 0.5f)
                    return new Vector2Int(i, lineIndex);
            }
            return new Vector2Int(selected.Text.Length, lineIndex);
        }

        void DoubleClickSelect(Vector2Int point)
        {
            if (point.y < 0 || point.y >= _selectionLines.Count) return;
            var line = _selectionLines[point.y];
            if (line.Text.Length == 0) { ClearSelection(); return; }

            int index = Mathf.Clamp(point.x, 0, line.Text.Length - 1);
            char anchor = line.Text[index];
            bool word = IsWordChar(anchor);
            int start = index;
            int end = index + 1;
            while (start > 0 && SameClass(line.Text[start - 1], anchor, word)) start--;
            while (end < line.Text.Length && SameClass(line.Text[end], anchor, word)) end++;

            _wordStart = new Vector2Int(start, point.y);
            _wordEnd = new Vector2Int(end, point.y);
            _selA = _wordStart;
            _selB = _wordEnd;
            _hasSel = true;
            _dragging = true;
            _wordDragging = true;
            CopySelection();
        }

        void UpdateWordSelection(Vector2Int point)
        {
            if (_selectionLines.Count == 0) return;
            int lineIndex = Mathf.Clamp(point.y, 0, _selectionLines.Count - 1);
            var line = _selectionLines[lineIndex];
            if (line.Text.Length == 0) return;

            int index = Mathf.Clamp(point.x, 0, line.Text.Length - 1);
            char anchor = line.Text[index];
            bool word = IsWordChar(anchor);
            int start = index;
            int end = index + 1;
            while (start > 0 && SameClass(line.Text[start - 1], anchor, word)) start--;
            while (end < line.Text.Length && SameClass(line.Text[end], anchor, word)) end++;

            var destinationStart = new Vector2Int(start, lineIndex);
            var destinationEnd = new Vector2Int(end, lineIndex);
            if (Before(point, _wordStart))
            {
                _selA = destinationStart;
                _selB = _wordEnd;
            }
            else
            {
                _selA = _wordStart;
                _selB = destinationEnd;
            }
            _hasSel = _selA != _selB;
        }

        void SelectLine(int line)
        {
            if (line < 0 || line >= _selectionLines.Count) return;
            int length = _selectionLines[line].Text.Length;
            if (length == 0) { ClearSelection(); return; }
            _selA = new Vector2Int(0, line);
            _selB = new Vector2Int(length, line);
            _hasSel = true;
            _dragging = false;
            _wordDragging = false;
            CopySelection();
        }

        static bool Before(Vector2Int a, Vector2Int b) =>
            a.y < b.y || (a.y == b.y && a.x < b.x);

        static bool SameClass(char c, char anchor, bool word) =>
            word ? IsWordChar(c) : c == anchor;

        static bool IsWordChar(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
            (c >= '0' && c <= '9') || c == '_';

        void ClearSelection()
        {
            _hasSel = false;
            _dragging = false;
            _wordDragging = false;
        }

        void SelectAll()
        {
            if (_selectionLines.Count == 0) return;
            _selA = Vector2Int.zero;
            int last = _selectionLines.Count - 1;
            _selB = new Vector2Int(_selectionLines[last].Text.Length, last);
            _hasSel = true;
            _dragging = false;
            _wordDragging = false;
            CopyText(SelectionText().TrimEnd('\n'));
        }

        void CopySelection()
        {
            if (_hasSel) CopyText(SelectionText());
        }

        string SelectionText()
        {
            if (_selectionLines.Count == 0 || !_hasSel) return "";
            OrderedSelection(out var a, out var b);
            int first = Mathf.Clamp(a.y, 0, _selectionLines.Count - 1);
            int last = Mathf.Clamp(b.y, 0, _selectionLines.Count - 1);
            var output = new System.Text.StringBuilder();
            for (int i = first; i <= last; i++)
            {
                string text = _selectionLines[i].Text;
                int start = i == a.y ? a.x : 0;
                int end = i == b.y ? b.x : text.Length;
                start = Mathf.Clamp(start, 0, text.Length);
                end = Mathf.Clamp(end, start, text.Length);
                if (end > start) output.Append(text.Substring(start, end - start));
                if (i < last) output.Append('\n');
            }
            return output.ToString();
        }

        void OrderedSelection(out Vector2Int a, out Vector2Int b)
        {
            a = _selA;
            b = _selB;
            if (Before(b, a))
            {
                var temp = a;
                a = b;
                b = temp;
            }
        }

        void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            GUIUtility.systemCopyBuffer = text;
            SlopClient.Post("/api/clipboard", "{\"text\":" + JVal.Q(text) + "}", null,
                msg => Log.Warning($"[SlopWorld] clipboard: {msg}"));
        }

        void OpenMenu()
        {
            var options = new List<FloatMenuOption>();
            var copy = new FloatMenuOption("Copy", CopySelection);
            copy.Disabled = !_hasSel;
            options.Add(copy);

            var paste = new FloatMenuOption("Paste", TerminalWindow.PasteClipboardToAgent);
            paste.Disabled = !TerminalWindow.CanPasteClipboardToAgent;
            options.Add(paste);
            options.Add(new FloatMenuOption("Select all", SelectAll));
            TerminalWindow.OpenOverPane(new SlopMenu(options));
        }

        static bool AllowedLink(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            return string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, "mailto", StringComparison.OrdinalIgnoreCase);
        }

        static List<MarkdownBlock> Parse(string source)
        {
            var document = Markdown.Parse(source ?? "", Pipeline, null);
            var blocks = new List<MarkdownBlock>();
            AddBlocks(document, blocks);
            return blocks;
        }

        static void AddBlocks(ContainerBlock container, List<MarkdownBlock> target)
        {
            foreach (Block block in container)
            {
                var converted = ConvertBlock(block);
                if (converted != null) target.Add(converted);
            }
        }

        static MarkdownBlock ConvertBlock(Block block)
        {
            if (block is BlankLineBlock) return null;

            // AutoIdentifiers stores heading anchors as document-level link definitions.
            // They are parser metadata, not visible blocks; stringifying them would leak
            // names such as HeadingLinkReferenceDefinition into the preview.
            if (block is LinkReferenceDefinition) return null;

            if (block is HeadingBlock heading)
                return new MarkdownBlock
                {
                    Kind = BlockKind.Heading,
                    Level = Mathf.Clamp(heading.Level, 1, 4),
                    Runs = ReadInlines(heading.Inline),
                };

            if (block is ParagraphBlock paragraph)
                return new MarkdownBlock
                {
                    Kind = BlockKind.Paragraph,
                    Runs = ReadInlines(paragraph.Inline),
                };

            if (block is FencedCodeBlock fenced)
                return new MarkdownBlock
                {
                    Kind = BlockKind.Code,
                    Code = fenced.Lines.ToString(),
                    Info = fenced.Info,
                };

            if (block is CodeBlock code)
                return new MarkdownBlock { Kind = BlockKind.Code, Code = code.Lines.ToString() };

            if (block is QuoteBlock quote)
            {
                var children = new List<MarkdownBlock>();
                AddBlocks(quote, children);
                return new MarkdownBlock { Kind = BlockKind.Quote, Children = children };
            }

            if (block is ListBlock list)
            {
                var children = new List<MarkdownBlock>();
                AddBlocks(list, children);
                int start = 1;
                int.TryParse(list.OrderedStart, out start);
                return new MarkdownBlock
                {
                    Kind = BlockKind.List,
                    Ordered = list.IsOrdered,
                    Start = start < 1 ? 1 : start,
                    Children = children,
                };
            }

            if (block is ListItemBlock item)
            {
                var children = new List<MarkdownBlock>();
                AddBlocks(item, children);
                return new MarkdownBlock { Kind = BlockKind.Item, Children = children };
            }

            if (block is ThematicBreakBlock)
                return new MarkdownBlock { Kind = BlockKind.Rule };

            if (block is Table table)
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
                return new MarkdownBlock { Kind = BlockKind.Table, Rows = rows };
            }

            if (block is HtmlBlock html)
            {
                var image = ParseImage(html.Lines.ToString());
                if (image != null)
                    return new MarkdownBlock
                    {
                        Kind = BlockKind.Paragraph,
                        Runs = new List<InlineRun> { image },
                    };

                return new MarkdownBlock
                {
                    Kind = BlockKind.Raw,
                    Runs = new List<InlineRun>
                    {
                        new InlineRun { Text = html.Lines.ToString(), Faint = true },
                    },
                };
            }

            if (block is ContainerBlock container)
            {
                var children = new List<MarkdownBlock>();
                AddBlocks(container, children);
                return new MarkdownBlock { Kind = BlockKind.Quote, Children = children };
            }

            return new MarkdownBlock
            {
                Kind = BlockKind.Raw,
                Runs = new List<InlineRun>
                {
                    new InlineRun { Text = block.ToString(), Faint = true },
                },
            };
        }

        static List<InlineRun> ReadCell(TableCell cell)
        {
            var runs = new List<InlineRun>();
            foreach (Block child in cell)
            {
                if (child is ParagraphBlock paragraph)
                    AppendInlines(paragraph.Inline, runs, false, false, false, null);
            }
            return runs;
        }

        static List<InlineRun> ReadInlines(ContainerInline inline)
        {
            var runs = new List<InlineRun>();
            AppendInlines(inline, runs, false, false, false, null);
            return runs;
        }

        static void AppendInlines(ContainerInline container, List<InlineRun> target,
                                   bool bold, bool italic, bool code, string link)
        {
            if (container == null) return;
            foreach (Inline inline in container)
            {
                if (inline is LiteralInline literal)
                {
                    AddRun(target, literal.Content.ToString(), bold, italic, code, link);
                }
                else if (inline is CodeInline codeInline)
                {
                    AddRun(target, codeInline.Content, bold, italic, true, link);
                }
                else if (inline is EmphasisInline emphasis)
                {
                    bool strong = emphasis.DelimiterCount >= 2;
                    AppendInlines(emphasis, target, bold || strong, italic || !strong, code, link);
                }
                else if (inline is LinkInline linkInline)
                {
                    string url = AllowedLink(linkInline.Url) ? linkInline.Url : null;
                    AppendInlines(linkInline, target, bold, italic, code, url);
                }
                else if (inline is AutolinkInline auto)
                {
                    string url = AllowedLink(auto.Url) ? auto.Url : null;
                    AddRun(target, auto.Url, bold, italic, code, url);
                }
                else if (inline is TaskList task)
                {
                    AddRun(target, task.Checked ? "[x] " : "[ ] ", bold, italic, code, link);
                }
                else if (inline is LineBreakInline)
                {
                    target.Add(new InlineRun { Text = "\n", Bold = bold, Italic = italic,
                        Code = code, Link = link });
                }
                else if (inline is HtmlInline html)
                {
                    var image = ParseImage(html.Tag);
                    if (image != null)
                    {
                        image.Bold = bold;
                        image.Italic = italic;
                        image.Code = code;
                        image.Link = link;
                        target.Add(image);
                    }
                    else AddRun(target, html.Tag, bold, italic, code, link);
                }
                else if (inline is ContainerInline nested)
                {
                    AppendInlines(nested, target, bold, italic, code, link);
                }
                else
                {
                    AddRun(target, inline.ToString(), bold, italic, code, link);
                }
            }
        }

        static InlineRun ParseImage(string html)
        {
            var match = ImageTag.Match(html ?? "");
            if (!match.Success) return null;

            string attrs = match.Groups["attrs"].Value;
            string source = HtmlAttribute(attrs, "src");
            if (string.IsNullOrWhiteSpace(source)) return null;

            return new InlineRun
            {
                ImagePath = HtmlDecode(source),
                ImageWidth = HtmlDimension(HtmlAttribute(attrs, "width")),
                ImageHeight = HtmlDimension(HtmlAttribute(attrs, "height")),
                ImageAlign = (HtmlAttribute(attrs, "align") ?? "").Trim().ToLowerInvariant(),
            };
        }

        static string HtmlAttribute(string attrs, string name)
        {
            string pattern = "(?:^|\\s)" + Regex.Escape(name) +
                "\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))";
            var match = Regex.Match(attrs ?? "", pattern, RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            if (match.Groups[1].Success) return match.Groups[1].Value;
            if (match.Groups[2].Success) return match.Groups[2].Value;
            return match.Groups[3].Value;
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

        static string HtmlDecode(string value) => (value ?? "")
            .Replace("&quot;", "\"")
            .Replace("&#34;", "\"")
            .Replace("&amp;", "&")
            .Replace("&#38;", "&")
            .Replace("&lt;", "<")
            .Replace("&#60;", "<")
            .Replace("&gt;", ">")
            .Replace("&#62;", ">")
            .Replace("&#39;", "'")
            .Replace("&apos;", "'");

        static void AddRun(List<InlineRun> target, string text, bool bold, bool italic,
                           bool code, string link)
        {
            if (string.IsNullOrEmpty(text)) return;
            target.Add(new InlineRun
            {
                Text = text,
                Bold = bold,
                Italic = italic,
                Code = code,
                Link = link,
            });
        }
    }
}
