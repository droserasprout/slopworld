using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SlopWorld
{
    enum BlockKind { Paragraph, Heading, Code, Quote, List, Item, Rule, Table, Raw }
    enum PlacementKind { Text, Image, Code, Rule, Quote, Table, Bullet }
    enum TextBreakKind { None, SoftWrap, Source }

    sealed class InlineRun
    {
        public string Text;
        public bool Bold;
        public bool Italic;
        public bool Code;
        public bool Strike;
        public bool Faint;
        public bool HasColor;
        public Color Color;
        public string Link;
        public string LocalLink;
        public bool IsTask;
        public bool TaskChecked;
        public bool IsImage;
        public string ImageAlt;
        public bool ImageFailed;
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
        public string Highlighted;
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
        public StringBuilder TextBuilder;
        public InlineRun Run;
        public GUIStyle Style;
        public float Width;
        public float Height;
    }

    sealed class TextLine
    {
        public readonly List<TextPiece> Pieces = new List<TextPiece>();
        public float Offset;
        public float Width;
        public float Height;
        public TextBreakKind BreakAfter;
        public bool Forced;
        public string CopySuffix;
        public int LogicalOffset;
        public int LogicalLength;
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
        public float Offset;
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
        public int Sequence;
    }

    sealed class SelectionLine
    {
        public int LogicalIndex;
        public float X;
        public float Y;
        public float Height;
        public float Width;
        public string Text;
        public TextLine Source;
        public bool CopyBreakAfter;
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

    struct ImageMetrics
    {
        public float Width;
        public float Height;

        public ImageMetrics(float width, float height)
        {
            Width = width;
            Height = height;
        }
    }

    static class MarkdownMarkup
    {
        public static string Decode(string value) => (value ?? "")
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
    }
}
