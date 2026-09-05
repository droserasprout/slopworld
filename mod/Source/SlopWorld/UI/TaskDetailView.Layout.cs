using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // TaskDetailView text wrapping and cached message geometry.
    public sealed partial class TaskDetailView
    {
        void EnsureLayout(float width, string body, string note)
        {
            string fontName = Settings.UIFontName ?? "";
            bool fontChanged = _layoutFontSize != Settings.UIFontSize ||
                _layoutFontName != fontName || !Mathf.Approximately(_layoutScale, Prefs.UIScale);
            if (_layoutTask == _task && Mathf.Approximately(_layoutWidth, width) &&
                !fontChanged) return;

            _layoutTask = _task;
            _layoutWidth = width;
            _layoutScale = Prefs.UIScale;
            _layoutFontSize = Settings.UIFontSize;
            _layoutFontName = fontName;

            int sourceLength = body.Length + (note.Length == 0 ? 0 : note.Length + 2);
            if (_selectionStart > sourceLength || _selectionEnd > sourceLength)
                ClearSelection();

            _bodyRanges.Clear();
            _bodyRanges.AddRange(WrappedRanges(body, TextWidth(width)));
            _bodyHeight = MessageCardHeight(_bodyRanges.Count);

            _noteRanges.Clear();
            _noteHeight = 0f;
            if (note.Length > 0)
            {
                _noteRanges.AddRange(WrappedRanges(note, TextWidth(width)));
                _noteHeight = SlopWidgets.GapM + MessageCardHeight(_noteRanges.Count);
            }

            _selectionLines.Clear();
            float bodyY = SlopWidgets.TinyRowH + SlopWidgets.GapS;
            float textY = bodyY + SlopWidgets.FieldPadY +
                SlopWidgets.LineHOf(GameFont.Tiny) + SlopWidgets.GapXS;
            CollectSelectableText(body, MessageTextX, textY, _bodyRanges, 0);
            if (note.Length > 0)
            {
                float noteY = bodyY + _bodyHeight + SlopWidgets.GapM;
                textY = noteY + SlopWidgets.FieldPadY +
                    SlopWidgets.LineHOf(GameFont.Tiny) + SlopWidgets.GapXS;
                CollectSelectableText(note, MessageTextX, textY, _noteRanges,
                    body.Length + 2);
            }
        }

        static float TextWidth(float width)
        {
            return Mathf.Max(1f, width - MessageTextX - SlopWidgets.FieldPadX);
        }

        List<TextRange> WrappedRanges(string text, float width)
        {
            text = text ?? "";
            var ranges = new List<TextRange>();
            if (text.Length == 0)
            {
                ranges.Add(new TextRange(0, 0));
                return ranges;
            }

            var wasFont = Text.Font;
            var wasWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = false;
                int start = 0;
                int lastBreak = -1;
                float lineWidth = 0f;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] == '\n')
                    {
                        ranges.Add(new TextRange(start, i));
                        start = i + 1;
                        lastBreak = -1;
                        lineWidth = 0f;
                        continue;
                    }

                    float charWidth = SmallCharWidth(text[i]);
                    if (lineWidth + charWidth <= width || i == start)
                    {
                        lineWidth += charWidth;
                        if (char.IsWhiteSpace(text[i])) lastBreak = i + 1;
                        continue;
                    }

                    int split = lastBreak > start ? lastBreak : i;
                    if (split <= start) split = Mathf.Min(start + 1, text.Length);
                    ranges.Add(new TextRange(start, split));
                    start = split;
                    lastBreak = -1;
                    lineWidth = 0f;
                    i = start - 1;
                }

                if (start <= text.Length) ranges.Add(new TextRange(start, text.Length));
                return ranges;
            }
            finally
            {
                Text.WordWrap = wasWrap;
                Text.Font = wasFont;
            }
        }

        float SmallCharWidth(char value)
        {
            float scale = Prefs.UIScale;
            string fontName = Settings.UIFontName ?? "";
            if (!Mathf.Approximately(_metricsScale, scale) ||
                _metricsFontSize != Settings.UIFontSize || _metricsFontName != fontName)
            {
                _metricsScale = scale;
                _metricsFontSize = Settings.UIFontSize;
                _metricsFontName = fontName;
                _smallCharWidths.Clear();
            }

            if (_smallCharWidths.TryGetValue(value, out var width)) return width;
            width = Text.CalcSize(value.ToString()).x;
            _smallCharWidths[value] = width;
            return width;
        }

        void CollectSelectableText(string text, float x, float y,
                                   List<TextRange> ranges, int sourceOffset)
        {
            text = text ?? "";
            float lineH = SlopWidgets.LineHOf(GameFont.Small);
            var wasFont = Text.Font;
            var wasWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = false;
                float lineY = y;
                foreach (var range in ranges)
                {
                    string lineText = text.Substring(range.Start, range.End - range.Start);
                    var edges = new float[lineText.Length + 1];
                    for (int i = 1; i < edges.Length; i++)
                        edges[i] = edges[i - 1] + SmallCharWidth(lineText[i - 1]);

                    _selectionLines.Add(new DialogueLine
                    {
                        Start = sourceOffset + range.Start,
                        End = sourceOffset + range.End,
                        X = x,
                        Y = lineY,
                        Width = edges[edges.Length - 1],
                        Height = lineH,
                        Text = lineText,
                        Edges = edges,
                    });
                    lineY += lineH;
                }
            }
            finally
            {
                Text.WordWrap = wasWrap;
                Text.Font = wasFont;
            }
        }
    }
}
