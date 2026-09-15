using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Converts inline runs into measurable lines. It knows nothing about blocks or drawing;
    // the flow layout and renderer consume this same geometry.
    sealed class MarkdownTextLayout
    {
        readonly StyleSet _styles;
        readonly Func<InlineRun, float, ImageMetrics> _imageMetrics;

        public MarkdownTextLayout(StyleSet styles,
                                  Func<InlineRun, float, ImageMetrics> imageMetrics)
        {
            _styles = styles;
            _imageMetrics = imageMetrics;
        }

        public TextLayout Wrap(List<InlineRun> runs, float width, int heading)
        {
            var layout = new TextLayout();
            var line = NewLine(_styles.Normal);

            foreach (var run in runs ?? new List<InlineRun>())
            {
                if (run.IsImage)
                {
                    ImageMetrics image = _imageMetrics(run, width);
                    if (line.Pieces.Count > 0 && (line.Width + image.Width > width || !string.IsNullOrEmpty(line.CopySuffix)))
                    {
                        line.BreakAfter = TextBreakKind.SoftWrap;
                        layout.Lines.Add(line);
                        line = NewLine(_styles.Normal);
                    }
                    AddPiece(line, run, "", _styles.Normal, image.Width, image.Height);
                    continue;
                }

                string text = run.Text ?? "";
                GUIStyle style = _styles.For(run, heading);
                if (run.IsTask)
                {
                    float taskWidth = UiControls.TickColW;
                    if (line.Pieces.Count > 0 && (line.Width + taskWidth > width || !string.IsNullOrEmpty(line.CopySuffix)))
                    {
                        line.BreakAfter = TextBreakKind.SoftWrap;
                        layout.Lines.Add(line);
                        line = NewLine(style);
                    }
                    AddPiece(line, run, text, style, taskWidth);
                    continue;
                }

                int start = 0;
                while (start <= text.Length)
                {
                    int newline = text.IndexOf('\n', start);
                    int end = newline < 0 ? text.Length : newline;
                    AppendWrapped(ref line, layout, run, text.Substring(start, end - start),
                        style, width, run.Code);
                    if (newline < 0) break;
                    line.BreakAfter = TextBreakKind.Source;
                    line.Forced = true;
                    layout.Lines.Add(line);
                    line = NewLine(style);
                    line.Forced = true;
                    start = newline + 1;
                }
            }

            if (line.Pieces.Count > 0 || line.Forced || layout.Lines.Count == 0)
                layout.Lines.Add(line);
            layout.Height = 0f;
            int logicalOffset = 0;
            foreach (var item in layout.Lines)
            {
                foreach (var piece in item.Pieces)
                {
                    if (piece.TextBuilder == null) continue;
                    piece.Text = piece.TextBuilder.ToString();
                    piece.TextBuilder = null;
                }
                item.LogicalOffset = logicalOffset;
                item.LogicalLength = 0;
                foreach (var piece in item.Pieces)
                    if (!piece.Run.IsImage) item.LogicalLength += piece.Text?.Length ?? 0;
                item.LogicalLength += item.CopySuffix?.Length ?? 0;
                logicalOffset += item.LogicalLength;
                if (item.BreakAfter == TextBreakKind.Source) logicalOffset++;
                if (item.Height <= 0f) item.Height = UiTheme.LineH;
                item.Offset = layout.Height;
                layout.Height += item.Height;
            }
            return layout;
        }

        TextLine NewLine(GUIStyle style) => new TextLine
        {
            Height = Mathf.Max(UiTheme.LineH, style?.lineHeight ?? UiTheme.LineH),
        };

        void AppendWrapped(ref TextLine line, TextLayout layout, InlineRun run, string text,
                           GUIStyle style, float width, bool preserveWhitespace)
        {
            if (preserveWhitespace)
            {
                AppendPreserved(ref line, layout, run, text, style, width);
                return;
            }

            int start = 0;
            while (start < text.Length)
            {
                bool space = char.IsWhiteSpace(text[start]);
                int end = start + 1;
                while (end < text.Length && char.IsWhiteSpace(text[end]) == space) end++;
                string chunk = text.Substring(start, end - start);
                float chunkWidth = MeasureChunk(style, chunk);

                if (space)
                {
                    if (line.Pieces.Count > 0 && line.Width + chunkWidth <= width &&
                        string.IsNullOrEmpty(line.CopySuffix))
                        AddPiece(line, run, chunk, style, chunkWidth);
                    else if (line.Pieces.Count > 0)
                        // Keep clipped spaces for copying across the wrap.
                        line.CopySuffix += chunk;
                }
                else if (line.Pieces.Count > 0 &&
                    (line.Width + chunkWidth > width || !string.IsNullOrEmpty(line.CopySuffix)))
                {
                    line.BreakAfter = TextBreakKind.SoftWrap;
                    layout.Lines.Add(line);
                    line = NewLine(style);
                    AddWord(ref line, layout, run, chunk, style, width, chunkWidth);
                }
                else
                {
                    AddWord(ref line, layout, run, chunk, style, width, chunkWidth);
                }

                start = end;
            }
        }

        void AppendPreserved(ref TextLine line, TextLayout layout, InlineRun run,
                             string text, GUIStyle style, float width)
        {
            for (int i = 0; i < text.Length; i++)
            {
                string value = text[i].ToString();
                float charWidth = MeasureChunk(style, value);
                if (line.Pieces.Count > 0 && (line.Width + charWidth > width || !string.IsNullOrEmpty(line.CopySuffix)))
                {
                    line.BreakAfter = TextBreakKind.SoftWrap;
                    layout.Lines.Add(line);
                    line = NewLine(style);
                }
                AddPiece(line, run, value, style, charWidth);
            }
        }

        void AddWord(ref TextLine line, TextLayout layout, InlineRun run, string word,
                     GUIStyle style, float width, float wordWidth)
        {
            if (wordWidth <= width)
            {
                AddPiece(line, run, word, style, wordWidth);
                return;
            }

            for (int i = 0; i < word.Length; i++)
            {
                float charWidth = _styles.MeasureChar(style, word[i]);
                if (line.Pieces.Count > 0 && (line.Width + charWidth > width || !string.IsNullOrEmpty(line.CopySuffix)))
                {
                    line.BreakAfter = TextBreakKind.SoftWrap;
                    layout.Lines.Add(line);
                    line = NewLine(style);
                }
                AddCharPiece(line, run, word[i], style, charWidth);
            }
        }

        static float Measure(GUIStyle style, string text) =>
            style.CalcSize(new GUIContent(text ?? "")).x;

        float MeasureChunk(GUIStyle style, string text)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            return text.Length > 128 ? MeasureCharacters(style, text) : Measure(style, text);
        }

        float MeasureCharacters(GUIStyle style, string text)
        {
            float width = 0f;
            for (int i = 0; i < text.Length; i++)
                width += _styles.MeasureChar(style, text[i]);
            return width;
        }

        static void AddPiece(TextLine line, InlineRun run, string text,
                             GUIStyle style, float width, float height = 0f)
        {
            if (string.IsNullOrEmpty(text) && !run.IsImage) return;

            if (!run.IsImage && line.Pieces.Count > 0)
            {
                var prior = line.Pieces[line.Pieces.Count - 1];
                if (!prior.Run.IsImage && ReferenceEquals(prior.Run, run) &&
                    ReferenceEquals(prior.Style, style))
                {
                    AppendText(prior, text);
                    prior.Width += width;
                    prior.Height = Mathf.Max(prior.Height,
                        height > 0f ? height : style.lineHeight);
                    line.Width += width;
                    line.Height = Mathf.Max(line.Height, prior.Height);
                    return;
                }
            }

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

        static void AddCharPiece(TextLine line, InlineRun run, char value,
                                 GUIStyle style, float width, float height = 0f)
        {
            if (line.Pieces.Count > 0)
            {
                var prior = line.Pieces[line.Pieces.Count - 1];
                if (!prior.Run.IsImage && ReferenceEquals(prior.Run, run) &&
                    ReferenceEquals(prior.Style, style))
                {
                    AppendChar(prior, value);
                    prior.Width += width;
                    prior.Height = Mathf.Max(prior.Height,
                        height > 0f ? height : style.lineHeight);
                    line.Width += width;
                    line.Height = Mathf.Max(line.Height, prior.Height);
                    return;
                }
            }
            AddPiece(line, run, value.ToString(), style, width, height);
        }

        static void AppendText(TextPiece piece, string text)
        {
            if (piece.TextBuilder == null)
            {
                piece.TextBuilder = new System.Text.StringBuilder(piece.Text ?? "");
                piece.Text = null;
            }
            piece.TextBuilder.Append(text);
        }

        static void AppendChar(TextPiece piece, char value)
        {
            if (piece.TextBuilder == null)
            {
                piece.TextBuilder = new System.Text.StringBuilder(piece.Text ?? "");
                piece.Text = null;
            }
            piece.TextBuilder.Append(value);
        }
    }
}
