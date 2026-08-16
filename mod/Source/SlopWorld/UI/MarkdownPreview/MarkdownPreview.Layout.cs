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
    // Mechanical split: MarkdownPreview.Layout methods.
    public sealed partial class MarkdownPreview
    {
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
                if (!run.IsImage || string.IsNullOrWhiteSpace(run.ImagePath)) continue;
                string path = ResolveImagePath(run.ImagePath);
                if (path == null)
                {
                    run.ImagePath = null;
                    run.ImageFailed = true;
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
                            run.ImageFailed = true;
                            _failedImages.Add(path);
                        }
                    },
                    msg =>
                    {
                        if (request != _request) return;
                        _pendingImages.Remove(path);
                        run.ImageFailed = true;
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
            try
            {
                string candidate = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(local)
                    ? local : System.IO.Path.Combine(baseDir, local));
                return IsInsideProject(candidate) ? candidate : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        Texture2D ImageFor(InlineRun image)
        {
            if (image == null || !image.IsImage || string.IsNullOrEmpty(image.ImagePath)) return null;
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
            bool right = image.ImageAlign == "right";
            _placements.Add(new Placement
            {
                Kind = PlacementKind.Text,
                X = right ? x : x + imageWidth + SlopWidgets.GapS,
                Y = y,
                Width = textWidth,
                Height = text.Height,
                Text = text,
            });
            _placements.Add(new Placement
            {
                Kind = PlacementKind.Image,
                X = right ? x + width - imageWidth : x,
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
                if (run.IsImage)
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
            if (string.IsNullOrEmpty(text) && !run.IsImage) return;
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

    }
}
