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

        public MarkdownLayoutEngine(MarkdownResourceStore resources)
        {
            _resources = resources;
        }

        public StyleSet Styles => _styles;
        public List<Placement> Placements => _placements;
        public float Width => _width;
        public float Height { get; private set; }

        public void EnsureStyles()
        {
            if (_styles != null) return;
            _styles = new StyleSet();
            _textLayout = new MarkdownTextLayout(_styles, ImageMetrics);
        }

        public void Invalidate()
        {
            _width = -1f;
        }

        public void Clear()
        {
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
            _placements.Clear();
            float y = UiTheme.GapM;
            float contentWidth = Mathf.Max(1f, width - UiTheme.GapM * 2f);
            foreach (var block in blocks ?? new List<MarkdownBlock>())
                y = Place(block, UiTheme.GapM, y, contentWidth);
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
                        return PlaceImage(image, x, y, width, UiTheme.GapS);
                    if (TryAlignedImage(block.Runs, out image))
                        return PlaceParagraphWithImage(block.Runs, image, x, y, width);
                    return PlaceText(block.Runs, x, y, width, 0, false, UiTheme.GapS);

                case BlockKind.Heading:
                    return PlaceText(block.Runs, x, y, width, block.Level, true, UiTheme.GapM);

                case BlockKind.Code:
                    return PlaceCode(block, x, y, width);

                case BlockKind.Raw:
                    return PlaceText(block.Runs, x, y, width, 0, false, UiTheme.GapS);

                case BlockKind.Rule:
                    _placements.Add(new Placement
                    {
                        Kind = PlacementKind.Rule,
                        X = x,
                        Y = y + UiTheme.GapS,
                        Width = width,
                        Height = 1f,
                    });
                    return y + UiTheme.GapS * 2f + 1f;

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
            ImageMetrics metrics = ImageMetrics(image, width);
            var textRuns = new List<InlineRun>();
            foreach (var run in runs)
                if (run != image) textRuns.Add(run);

            bool hasText = false;
            foreach (var run in textRuns)
                if (!string.IsNullOrWhiteSpace(run.Text)) { hasText = true; break; }
            if (hasText && metrics.Width + UiTheme.GapS >= width)
            {
                PlaceImage(image, x, y, width, UiTheme.GapS);
                return PlaceText(textRuns, x, y + metrics.Height + UiTheme.GapS,
                    width, 0, false, UiTheme.GapS);
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
            return y + Mathf.Max(text.Height, metrics.Height) + UiTheme.GapS;
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
            return y + height + UiTheme.GapS;
        }

        static List<InlineRun> CodeRuns(MarkdownBlock block)
        {
            var runs = new List<InlineRun>();
            if (string.IsNullOrEmpty(block.Highlighted))
            {
                runs.Add(new InlineRun { Text = block.Code ?? "", Code = true });
                return runs;
            }

            string[] lines = block.Highlighted.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (var run in Sgr.ParseLine(lines[i]))
                    runs.Add(new InlineRun
                    {
                        Text = run.Text,
                        Code = true,
                        HasColor = true,
                        Color = run.Fg,
                    });
                if (i + 1 < lines.Length)
                    runs.Add(new InlineRun { Text = "\n", Code = true });
            }
            return runs;
        }

        float PlaceQuote(MarkdownBlock block, float x, float y, float width)
        {
            float start = y;
            float innerX = x + UiTheme.GapM;
            float innerWidth = Mathf.Max(1f, width - UiTheme.GapM);
            foreach (var child in block.Children)
                y = Place(child, innerX, y, innerWidth);

            _placements.Add(new Placement
            {
                Kind = PlacementKind.Quote,
                X = x,
                Y = start,
                Width = width,
                Height = Mathf.Max(UiTheme.LineH, y - start - UiTheme.GapS),
            });
            return y + UiTheme.GapS;
        }

        float PlaceList(MarkdownBlock block, float x, float y, float width)
        {
            int number = block.Start;
            foreach (var item in block.Children)
            {
                string marker = block.Ordered ? number++ + "." : "•";
                var bullet = new List<InlineRun> { new InlineRun { Text = marker } };
                var bulletText = _textLayout.Wrap(bullet, width, 0);
                _placements.Add(new Placement
                {
                    Kind = PlacementKind.Bullet,
                    X = x,
                    Y = y,
                    Width = UiTheme.GapL,
                    Height = bulletText.Height,
                    Text = bulletText,
                });

                float itemY = y;
                float innerX = x + UiTheme.GapL;
                float innerWidth = Mathf.Max(1f, width - UiTheme.GapL);
                foreach (var child in item.Children)
                    itemY = Place(child, innerX, itemY, innerWidth);
                y = Mathf.Max(itemY - UiTheme.GapS, y + bulletText.Height) + 1f;
            }
            return y + 1f;
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
            return y + table.Height + UiTheme.GapM;
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
                    var text = _textLayout.Wrap(cell,
                        Mathf.Max(1f, cellWidth - UiTheme.GapS * 2f), 0);
                    result.Cells.Add(text);
                    result.Height = Mathf.Max(result.Height,
                        text.Height + UiTheme.GapS * 2f);
                }
                while (result.Cells.Count < columns)
                    result.Cells.Add(_textLayout.Wrap(new List<InlineRun>(),
                        Mathf.Max(1f, cellWidth - UiTheme.GapS * 2f), 0));
                result.Offset = table.Height;
                table.Rows.Add(result);
                table.Height += result.Height;
            }
            for (int i = 0; i < columns; i++) table.Widths[i] = cellWidth;
            return table;
        }
    }
}
