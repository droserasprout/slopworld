using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Places block-level content. Inline wrapping lives in MarkdownTextLayout so this class
    // only has to deal with document flow, indentation, images, quotes, lists, and tables.
    sealed class MarkdownLayoutEngine
    {
        readonly MarkdownResourceStore _resources;
        readonly List<Placement> _placements = new List<Placement>();
        StyleSet _styles;
        MarkdownTextLayout _textLayout;
        float _width = -1f;
        int _terminalRevision = -1;

        public MarkdownLayoutEngine(MarkdownResourceStore resources)
        {
            _resources = resources;
        }

        public StyleSet Styles => _styles;
        public List<Placement> Placements => _placements;
        public int Generation { get; private set; }
        public float Width => _width;
        public float Height { get; private set; }

        public void EnsureStyles()
        {
            // Terminal settings have their own revision, independent of chrome metrics.
            if (_styles != null)
            {
                if (_terminalRevision != TerminalFont.Rev)
                {
                    InvalidateTypography();
                    _terminalRevision = TerminalFont.Rev;
                }
                return;
            }
            _styles = new StyleSet();
            _terminalRevision = TerminalFont.Rev;
            _textLayout = new MarkdownTextLayout(_styles, ImageMetrics);
        }

        public void Invalidate()
        {
            _width = -1f;
        }

        public void InvalidateTypography()
        {
            if (_textLayout != null) _textLayout.InvalidateMetrics();
            _width = -1f;
        }

        public void Clear()
        {
            Generation++;
            _placements.Clear();
            _width = -1f;
            Height = 0f;
        }

        public void Reflow(List<MarkdownBlock> blocks, float width)
        {
            EnsureStyles();
            width = Mathf.Max(1f, width);
            if (Mathf.Approximately(width, _width)) return;

            _width = width;
            Generation++;
            _placements.Clear();
            float y = UiTheme.GapM;
            float contentWidth = Mathf.Max(1f, width - UiTheme.GapM * 2f);
            foreach (var block in blocks ?? new List<MarkdownBlock>())
            {
                y = Place(block, UiTheme.GapM, y, contentWidth);
                y += BlockGap(block);
            }
            Height = Mathf.Max(1f, y + UiTheme.GapM);
            for (int i = 0; i < _placements.Count; i++)
                _placements[i].Sequence = i;
            _placements.Sort((left, right) =>
            {
                int result = left.Y.CompareTo(right.Y);
                return result != 0 ? result : left.Sequence.CompareTo(right.Sequence);
            });
        }

        ImageMetrics ImageMetrics(InlineRun image, float available)
        {
            var texture = _resources.ImageFor(image);
            float naturalWidth = texture == null ? 320f : texture.width;
            float naturalHeight = texture == null ? 180f : texture.height;

            bool explicitWidth = image.ImageWidth > 0f;
            bool explicitHeight = image.ImageHeight > 0f;
            float width = explicitWidth ? image.ImageWidth : naturalWidth;
            float height = explicitHeight ? image.ImageHeight : naturalHeight;
            float aspect = naturalHeight / Mathf.Max(1f, naturalWidth);
            if (explicitWidth && !explicitHeight) height = width * aspect;
            else if (explicitHeight && !explicitWidth) width = height / Mathf.Max(.001f, aspect);

            // Fit the requested box proportionally, including explicit HTML dimensions.
            float maxWidth = Mathf.Max(1f, available);
            if (width > maxWidth)
            {
                float scale = maxWidth / width;
                width *= scale;
                height *= scale;
            }
            return new ImageMetrics(width, Mathf.Max(1f, height));
        }

        float Place(MarkdownBlock block, float x, float y, float width)
        {
            switch (block.Kind)
            {
                case BlockKind.Paragraph:
                    if (TrySingleImage(block.Runs, out var image))
                        return PlaceImage(image, x, y, width, 0f);
                    if (TryAlignedImage(block.Runs, out image))
                        return PlaceParagraphWithImage(block.Runs, image, x, y, width);
                    return PlaceText(block.Runs, x, y, width, 0, false, 0f);

                case BlockKind.Heading:
                    return PlaceHeading(block.Runs, x, y, width, block.Level);

                case BlockKind.Code:
                    return PlaceCode(block, x, y, width);

                case BlockKind.Raw:
                    return PlaceText(block.Runs, x, y, width, 0, false, 0f);

                case BlockKind.Rule:
                    _placements.Add(new Placement
                    {
                        Kind = PlacementKind.Rule,
                        X = x,
                        Y = y + UiTheme.GapS,
                        Width = width,
                        Height = 1f,
                    });
                    return y + UiTheme.GapS + 1f;

                case BlockKind.Quote:
                    return PlaceQuote(block, x, y, width);

                case BlockKind.List:
                    return PlaceList(block, x, y, width);

                case BlockKind.Table:
                    return PlaceTable(block, x, y, width);

                case BlockKind.Item:
                    foreach (var child in block.Children ?? new List<MarkdownBlock>())
                    {
                        y = Place(child, x, y, width);
                        y += BlockGap(child);
                    }
                    return y;

                default:
                    return y;
            }
        }

        float PlaceParagraphWithImage(List<InlineRun> runs, InlineRun image,
                                      float x, float y, float width)
        {
            ImageMetrics metrics = ImageMetrics(image, width);
            var textRuns = new List<InlineRun>();
            foreach (var run in runs)
                if (run != image) textRuns.Add(run);

            bool hasText = false;
            foreach (var run in textRuns)
                if (!string.IsNullOrWhiteSpace(run.Text)) { hasText = true; break; }
            if (hasText && metrics.Width + UiTheme.GapS >= width)
            {
                PlaceImage(image, x, y, width, 0f);
                return PlaceText(textRuns, x, y + metrics.Height + UiTheme.GapS,
                    width, 0, false, 0f);
            }

            float textWidth = Mathf.Max(1f, width - metrics.Width - UiTheme.GapS);
            var text = _textLayout.Wrap(textRuns, textWidth, 0);
            bool right = image.ImageAlign == "right";
            _placements.Add(new Placement
            {
                Kind = PlacementKind.Text,
                X = right ? x : x + metrics.Width + UiTheme.GapS,
                Y = y,
                Width = textWidth,
                Height = text.Height,
                Text = text,
            });
            _placements.Add(new Placement
            {
                Kind = PlacementKind.Image,
                X = right ? x + width - metrics.Width : x,
                Y = y,
                Width = metrics.Width,
                Height = metrics.Height,
                Image = image,
            });
            return y + Mathf.Max(text.Height, metrics.Height);
        }

        float PlaceImage(InlineRun image, float x, float y, float width, float gap)
        {
            ImageMetrics metrics = ImageMetrics(image, width);
            float imageX = image.ImageAlign == "right"
                ? x + width - metrics.Width
                : image.ImageAlign == "center"
                    ? x + (width - metrics.Width) / 2f
                    : x;
            _placements.Add(new Placement
            {
                Kind = PlacementKind.Image,
                X = imageX,
                Y = y,
                Width = metrics.Width,
                Height = metrics.Height,
                Image = image,
            });
            return y + metrics.Height + gap;
        }

        static bool TrySingleImage(List<InlineRun> runs, out InlineRun image)
        {
            image = null;
            foreach (var run in runs ?? new List<InlineRun>())
            {
                if (run.IsImage)
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
                if (!run.IsImage) continue;
                if (image != null || (run.ImageAlign != "left" && run.ImageAlign != "right"))
                    return false;
                image = run;
            }
            return image != null;
        }

        float PlaceText(List<InlineRun> runs, float x, float y, float width,
                        int heading, bool headingColor, float gap)
        {
            var text = _textLayout.Wrap(runs, width, heading);
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

        float PlaceHeading(List<InlineRun> runs, float x, float y, float width, int level)
        {
            // The initial document inset already provides top breathing room. Later headings
            // get a larger leading margin than the body gap that follows them.
            if (y > UiTheme.GapM + .01f) y += UiTheme.GapM;
            return PlaceText(runs, x, y, width, level, true, 0f);
        }

        static float BlockGap(MarkdownBlock block)
        {
            if (block == null) return 0f;
            return block.Kind == BlockKind.Table ? UiTheme.GapM : UiTheme.GapS;
        }

        float PlaceCode(MarkdownBlock block, float x, float y, float width)
        {
            var runs = CodeRuns(block);
            var text = _textLayout.Wrap(runs, Mathf.Max(1f, width - UiTheme.GapS * 2f), 0);
            float labelHeight = string.IsNullOrWhiteSpace(block.Info)
                ? 0f : UiTheme.TinyH + UiTheme.GapXS;
            float height = text.Height + UiTheme.GapS * 2f + labelHeight;
            _placements.Add(new Placement
            {
                Kind = PlacementKind.Code,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Text = text,
                Label = block.Info,
            });
            return y + height;
        }

        static List<InlineRun> CodeRuns(MarkdownBlock block)
        {
            var runs = new List<InlineRun>();
            if (string.IsNullOrEmpty(block.Highlighted))
            {
                runs.Add(new InlineRun { Text = block.Code ?? "", Code = true });
                return runs;
            }

            var plain = new System.Text.StringBuilder();
            string[] lines = block.Highlighted.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (var run in Sgr.ParseLine(lines[i]))
                {
                    plain.Append(run.Text);
                    runs.Add(new InlineRun
                    {
                        Text = run.Text,
                        Code = true,
                        HasColor = true,
                        Color = run.Fg,
                    });
                }
                if (i + 1 < lines.Length)
                {
                    plain.Append('\n');
                    runs.Add(new InlineRun { Text = "\n", Code = true });
                }
            }
            // A highlighter is allowed to normalize or reject input. In that case keeping its
            // output would make selection/copy silently differ from the source document.
            return string.Equals(plain.ToString(), block.Code ?? "",
                System.StringComparison.Ordinal)
                ? runs : new List<InlineRun> { new InlineRun { Text = block.Code ?? "", Code = true } };
        }

        float PlaceQuote(MarkdownBlock block, float x, float y, float width)
        {
            float start = y;
            float innerX = x + UiTheme.GapM;
            float innerWidth = Mathf.Max(1f, width - UiTheme.GapM);
            foreach (var child in block.Children ?? new List<MarkdownBlock>())
            {
                y = Place(child, innerX, y, innerWidth);
                y += BlockGap(child);
            }

            _placements.Add(new Placement
            {
                Kind = PlacementKind.Quote,
                X = x,
                Y = start,
                Width = width,
                Height = Mathf.Max(UiTheme.LineH, y - start),
            });
            return y;
        }

        float PlaceList(MarkdownBlock block, float x, float y, float width)
        {
            int number = block.Start;
            var markers = new List<List<InlineRun>>();
            float markerWidth = 0f;
            foreach (var item in block.Children ?? new List<MarkdownBlock>())
            {
                string marker = block.Ordered ? number++ + "." : "•";
                var bullet = new List<InlineRun> { new InlineRun { Text = marker } };
                markers.Add(bullet);
                markerWidth = Mathf.Max(markerWidth, _textLayout.Measure(bullet, 0).PreferredWidth);
            }

            // Reserve one stable gutter for the widest marker. A marker that grows from 9 to
            // 10 must not move the item text or collide with it halfway through the list.
            float gutter = MarkdownTableGeometry.MarkerGutter(width, markerWidth);
            int itemIndex = 0;
            foreach (var item in block.Children ?? new List<MarkdownBlock>())
            {
                var bulletText = _textLayout.Wrap(markers[itemIndex++], gutter, 0);
                _placements.Add(new Placement
                {
                    Kind = PlacementKind.Bullet,
                    X = x,
                    Y = y,
                    Width = gutter,
                    Height = bulletText.Height,
                    Text = bulletText,
                });

                float itemY = y;
                float innerX = x + gutter;
                float innerWidth = Mathf.Max(1f, width - gutter);
                var children = item?.Children ?? new List<MarkdownBlock>();
                for (int childIndex = 0; childIndex < children.Count; childIndex++)
                {
                    var child = children[childIndex];
                    itemY = Place(child, innerX, itemY, innerWidth);
                    bool last = childIndex + 1 == children.Count;
                    if (!last)
                    {
                        var next = children[childIndex + 1];
                        itemY += GapBetweenListChildren(block.Tight, child, next);
                    }
                }
                float itemGap = block.Tight ? UiTheme.GapXS : UiTheme.GapM;
                y = Mathf.Max(itemY, y + bulletText.Height) + itemGap;
            }
            return y;
        }

        static float GapBetweenListChildren(bool tight, MarkdownBlock child, MarkdownBlock next)
        {
            if (!tight) return BlockGap(child);
            return child.Kind == BlockKind.List || next.Kind == BlockKind.List ? UiTheme.GapS : 0f;
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
            return y + table.Height;
        }

        TableLayout MakeTable(MarkdownBlock block, float width)
        {
            int columns = 0;
            foreach (var row in block.Rows ?? new List<TableRow>())
                columns = Mathf.Max(columns, row.Cells.Count);
            columns = Mathf.Max(1, columns);

            var minimum = new float[columns];
            var preferred = new float[columns];
            for (int i = 0; i < columns; i++) minimum[i] = preferred[i] = UiTheme.GapS * 2f + 1f;
            foreach (var row in block.Rows ?? new List<TableRow>())
            {
                for (int i = 0; i < row.Cells.Count && i < columns; i++)
                {
                    MarkdownTextMetrics metrics = _textLayout.Measure(row.Cells[i], 0);
                    minimum[i] = Mathf.Max(minimum[i], metrics.MinimumWidth + UiTheme.GapS * 2f);
                    preferred[i] = Mathf.Max(preferred[i], metrics.PreferredWidth + UiTheme.GapS * 2f);
                }
            }

            var table = new TableLayout
            {
                Widths = MarkdownTableGeometry.AllocateColumns(width, minimum, preferred),
                Alignments = new TableAlignment[columns],
            };
            for (int i = 0; i < columns; i++)
                table.Alignments[i] = block.ColumnAlignments != null &&
                    i < block.ColumnAlignments.Count
                    ? block.ColumnAlignments[i] : TableAlignment.Left;

            foreach (var row in block.Rows ?? new List<TableRow>())
            {
                var result = new TableRowLayout { Header = row.Header };
                for (int i = 0; i < row.Cells.Count && i < columns; i++)
                {
                    float cellWidth = table.Widths[i];
                    float padding = MarkdownTableGeometry.Padding(cellWidth);
                    var text = _textLayout.Wrap(row.Cells[i],
                        MarkdownTableGeometry.InnerWidth(cellWidth), 0);
                    result.Cells.Add(text);
                    result.Height = Mathf.Max(result.Height,
                        text.Height + padding * 2f);
                }
                while (result.Cells.Count < columns)
                {
                    int i = result.Cells.Count;
                    float cellWidth = table.Widths[i];
                    float padding = MarkdownTableGeometry.Padding(cellWidth);
                    result.Cells.Add(_textLayout.Wrap(new List<InlineRun>(),
                        MarkdownTableGeometry.InnerWidth(cellWidth), 0));
                    result.Height = Mathf.Max(result.Height, padding * 2f + UiTheme.LineH);
                }
                result.Offset = table.Height;
                table.Rows.Add(result);
                table.Height += result.Height;
            }
            return table;
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
