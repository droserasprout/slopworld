using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SlopWorld
{
    enum BlockKind { Paragraph, Heading, Code, Quote, List, Item, Rule, Table, Raw }
    enum PlacementKind { Text, Image, Code, Rule, Quote, Table, Bullet }
    enum TextBreakKind { None, SoftWrap, Source }
    enum TableAlignment { Left, Center, Right }

    sealed class InlineRun
    {
        public string Text;
        public bool Bold;
        public bool Italic;
        public bool Code;
        public bool InlineCode;
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
        public bool ImageFailed = false;
        public string ImagePath;
        public float ImageWidth;
        public float ImageHeight;
        public string ImageAlign;
    }

    sealed class MarkdownBlock
    {
        public BlockKind Kind;
        public int Level;
        public string Alignment;
        public bool Ordered;
        public bool Tight = true;
        public int Start;
        public string Code;
        public string Info;
        public string Highlighted = null;
        public List<InlineRun> Runs;
        public List<MarkdownBlock> Children;
        public List<TableRow> Rows;
        public List<TableAlignment> ColumnAlignments;
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
        public float PaddingLeft;
        public float PaddingRight;
        public float OffsetY;
        public float Baseline;
    }

    sealed class TextLine
    {
        public readonly List<TextPiece> Pieces = new List<TextPiece>();
        public float Offset;
        public float OffsetX;
        public float Width;
        public float Height;
        public TextBreakKind BreakAfter;
        public bool Forced;
        public bool Continuation;
        public string CopySuffix;
        public int LogicalOffset;
        public int LogicalLength;
    }

    sealed class TextLayout
    {
        public readonly List<TextLine> Lines = new List<TextLine>();
        public float Width;
        public float Height;
    }

    sealed class TableLayout
    {
        public readonly List<TableRowLayout> Rows = new List<TableRowLayout>();
        public float[] Widths;
        public TableAlignment[] Alignments;
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

    sealed class SelectionLine : DocumentSelectionLine
    {
        public TextLine Source;
        public bool CopyBreakAfter;
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

    struct MarkdownTextMetrics
    {
        public float MinimumWidth;
        public float PreferredWidth;

        public MarkdownTextMetrics(float minimumWidth, float preferredWidth)
        {
            MinimumWidth = minimumWidth;
            PreferredWidth = preferredWidth;
        }
    }

}
