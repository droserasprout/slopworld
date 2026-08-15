using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A deliberately native Markdown reader. Markdig supplies the CommonMark/GFM parse tree;
    // this class supplies the small, scheme-coloured document layout that belongs in the game.
    // It never turns Markdown into HTML: the small native renderer handles a deliberately
    // narrow, safe HTML tag subset itself, while other HTML remains literal, faint text.
    // Links are checked again before they reach the daemon or the local Files viewer.
    public sealed partial class MarkdownPreview : IContentView
    {
        enum BlockKind { Paragraph, Heading, Code, Quote, List, Item, Rule, Table, Raw }
        enum PlacementKind { Text, Image, Code, Rule, Quote, Table, Bullet }

        sealed class InlineRun
        {
            public string Text;
            public bool Bold;
            public bool Italic;
            public bool Code;
            public bool Strike;
            public bool Faint;
            public string Link;
            public string LocalLink;
            public bool IsImage;
            public bool ImageFailed;
            public string ImagePath;
            public float ImageWidth;
            public float ImageHeight;
            public string ImageAlign;
        }

        sealed class HtmlState
        {
            public int Bold;
            public int Italic;
            public int Code;
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
            public string LocalPath;

            public LinkHit(Rect rect, string url, string localPath)
            {
                Rect = rect;
                Url = url;
                LocalPath = localPath;
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
                    // TerminalWindow reuses TerminalFont.Style and mutates its normal text
                    // color for every ANSI run. Do not inherit the last terminal foreground;
                    // DrawText applies the Markdown scheme color through GUI.color.
                    Code.normal.textColor = Color.white;
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

        readonly string _project;
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
            _project = project;
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

}
}
