using System.Collections.Generic;
using UnityEngine;
using Verse;
using TextRange = SlopWorld.TextElementLayout.Range;

namespace SlopWorld
{
    public sealed partial class TaskDetailView
    {
        void EnsureLayout(float width, string body, string note)
        {
            string fontName = Settings.UIFontName ?? "";
            int layoutRevision = WorkspaceLayout.Revision;
            bool fontChanged = _layoutFontSize != Settings.UIFontSize ||
                _layoutFontName != fontName || !Mathf.Approximately(_layoutScale, Prefs.UIScale);
            if (_layoutTask == _task && Mathf.Approximately(_layoutWidth, width) &&
                !fontChanged && _layoutRevision == layoutRevision) return;

            _layoutTask = _task;
            _layoutWidth = width;
            _layoutScale = Prefs.UIScale;
            _layoutFontSize = Settings.UIFontSize;
            _layoutFontName = fontName;
            _layoutRevision = layoutRevision;

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
                _noteHeight = UiTheme.GapM + MessageCardHeight(_noteRanges.Count);
            }

            _selectionLines.Clear();
            float bodyY = UiTheme.TinyRowH + UiTheme.GapS;
            float textY = bodyY + UiTheme.FieldPadY +
                UiTheme.LineHOf(GameFont.Tiny) + UiTheme.GapXS;
            CollectSelectableText(body, MessageTextX, textY, _bodyRanges, 0);
            if (note.Length > 0)
            {
                float noteY = bodyY + _bodyHeight + UiTheme.GapM;
                textY = noteY + UiTheme.FieldPadY +
                    UiTheme.LineHOf(GameFont.Tiny) + UiTheme.GapXS;
                CollectSelectableText(note, MessageTextX, textY, _noteRanges,
                    body.Length + 2);
            }
        }

        static float TextWidth(float width)
        {
            return Mathf.Max(1f, width - MessageTextX - UiTheme.FieldPadX);
        }

        List<TextRange> WrappedRanges(string text, float width)
        {
            var wasFont = Text.Font;
            var wasWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = false;
                return TextElementLayout.Wrap(text, width, SmallElementWidth);
            }
            finally
            {
                Text.WordWrap = wasWrap;
                Text.Font = wasFont;
            }
        }

        float SmallElementWidth(string value)
        {
            float scale = Prefs.UIScale;
            string fontName = Settings.UIFontName ?? "";
            if (!Mathf.Approximately(_metricsScale, scale) ||
                _metricsFontSize != Settings.UIFontSize || _metricsFontName != fontName)
            {
                _metricsScale = scale;
                _metricsFontSize = Settings.UIFontSize;
                _metricsFontName = fontName;
                _smallElementWidths.Clear();
            }

            if (_smallElementWidths.TryGetValue(value, out var width)) return width;
            width = Text.CalcSize(value).x;
            _smallElementWidths[value] = width;
            return width;
        }

        void CollectSelectableText(string text, float x, float y,
                                   List<TextRange> ranges, int sourceOffset)
        {
            text = text ?? "";
            float lineH = UiTheme.LineHOf(GameFont.Small);
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
                    var boundaries = TextElementLayout.Boundaries(lineText);
                    for (int i = 0; i < boundaries.Length - 1; i++)
                    {
                        int start = boundaries[i], end = boundaries[i + 1];
                        for (int offset = start + 1; offset < end; offset++) edges[offset] = edges[start];
                        edges[end] = edges[start] + SmallElementWidth(lineText.Substring(start, end - start));
                    }

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
                        Boundaries = boundaries,
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
