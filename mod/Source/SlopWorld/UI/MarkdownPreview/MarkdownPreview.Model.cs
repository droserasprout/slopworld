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

    static class MarkdownTableGeometry
    {
        public static float MarkerGutter(float width, float markerWidth) =>
            Mathf.Min(width, markerWidth + UiTheme.GapS);

        public static float Padding(float cellWidth) =>
            Mathf.Min(UiTheme.GapS, Mathf.Max(0f, (cellWidth - 1f) / 2f));

        public static float InnerWidth(float cellWidth)
        {
            float padding = Padding(cellWidth);
            return Mathf.Max(0f, cellWidth - padding * 2f);
        }

        public static float TextX(TableLayout table, float x, int cellIndex,
                                  TextLayout text)
        {
            float cellWidth = table.Widths[cellIndex];
            float padding = Padding(cellWidth);
            float innerX = x + padding;
            TableAlignment alignment = table.Alignments != null &&
                cellIndex < table.Alignments.Length
                ? table.Alignments[cellIndex] : TableAlignment.Left;
            return AlignX(innerX, InnerWidth(cellWidth), text?.Width ?? 0f, alignment);
        }

        public static float AlignX(float x, float availableWidth, float contentWidth,
                                   TableAlignment alignment)
        {
            float spare = Mathf.Max(0f, availableWidth - contentWidth);
            if (alignment == TableAlignment.Center) return x + spare / 2f;
            if (alignment == TableAlignment.Right) return x + spare;
            return x;
        }

        public static float[] AllocateColumns(float width, float[] minimum, float[] preferred)
        {
            int count = minimum.Length;
            var result = new float[count];
            float totalMinimum = 0f;
            for (int i = 0; i < count; i++) totalMinimum += minimum[i];
            if (totalMinimum > width)
            {
                float scale = width / Mathf.Max(1f, totalMinimum);
                for (int i = 0; i < count; i++) result[i] = minimum[i] * scale;
                return result;
            }

            for (int i = 0; i < count; i++) result[i] = minimum[i];
            float remaining = width - totalMinimum;
            while (remaining > .01f)
            {
                int active = 0;
                for (int i = 0; i < count; i++)
                    if (result[i] + .01f < preferred[i]) active++;
                if (active == 0)
                {
                    float share = remaining / count;
                    for (int i = 0; i < count; i++) result[i] += share;
                    break;
                }

                float activeShare = remaining / active;
                float consumed = 0f;
                for (int i = 0; i < count; i++)
                {
                    if (result[i] + .01f >= preferred[i]) continue;
                    float add = Mathf.Min(activeShare, preferred[i] - result[i]);
                    result[i] += add;
                    consumed += add;
                }
                if (consumed <= .01f) break;
                remaining -= consumed;
            }
            return result;
        }
    }

}
