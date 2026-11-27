using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using TextRange = SlopWorld.TextElementLayout.Range;

namespace SlopWorld
{
    internal sealed partial class ConversationReader
    {
        void EnsureLayout(
            float width,
            object source,
            IEnumerable<(string Body, string Sender, long Time, string Status)> entries,
            bool hasHeading
        )
        {
            string fontName = Settings.UIFontName ?? "";
            int layoutRevision = WorkspaceLayout.Revision;
            bool fontChanged =
                _layoutFontSize != Settings.UIFontSize
                || _layoutFontName != fontName
                || !Mathf.Approximately(_layoutScale, Prefs.UIScale);
            if (
                ReferenceEquals(_layoutSource, source)
                && Mathf.Approximately(_layoutWidth, width)
                && !fontChanged
                && _layoutRevision == layoutRevision
            )
                return;

            _layoutSource = source;
            _layoutWidth = width;
            _layoutScale = Prefs.UIScale;
            _layoutFontSize = Settings.UIFontSize;
            _layoutFontName = fontName;
            _layoutRevision = layoutRevision;

            int anchor = _selection.AnchorOffset,
                focus = _selection.FocusOffset;

            _messages.Clear();
            _selectionLines.Clear();
            var text = new StringBuilder();
            float y = hasHeading ? UiTheme.TinyRowH + UiTheme.GapS : 0;
            foreach (var entry in entries)
                AddMessage(width, entry.Body, entry.Sender, entry.Time, entry.Status, text, ref y);
            _contentHeight = y + UiTheme.GapS;
            _selection.Rebuilt(text.ToString(), anchor, focus);
        }

        void AddMessage(
            float width,
            string body,
            string sender,
            long timestamp,
            string status,
            StringBuilder text,
            ref float y
        )
        {
            if (_messages.Count > 0)
            {
                y += UiTheme.GapM;
                text.Append("\n\n");
            }
            var ranges = WrappedRanges(body, TextWidth(width));
            bool timestampBelow;
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Tiny;
                timestampBelow =
                    timestamp > 0
                    && UiTheme.Wide(
                        Prefix(status) + SenderLabel(sender) + "  ·  " + Timestamp(timestamp)
                    ) > TextWidth(width);
            }
            float headerH = UiTheme.LineHOf(GameFont.Tiny) * (timestampBelow ? 2 : 1);
            float height = MessageCardHeight(ranges.Count, headerH);
            _messages.Add(
                new MessageLayout
                {
                    Sender = sender,
                    Timestamp = timestamp,
                    Status = status,
                    TimestampBelow = timestampBelow,
                    Y = y,
                    Height = height,
                }
            );
            float textY = y + UiTheme.FieldPadY + headerH + UiTheme.GapXS;
            CollectSelectableText(body, MessageTextX, textY, ranges, text.Length);
            text.Append(body);
            y += height;
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
            if (
                !Mathf.Approximately(_metricsScale, scale)
                || _metricsFontSize != Settings.UIFontSize
                || _metricsFontName != fontName
            )
            {
                _metricsScale = scale;
                _metricsFontSize = Settings.UIFontSize;
                _metricsFontName = fontName;
                _smallElementWidths.Clear();
            }

            if (_smallElementWidths.TryGetValue(value, out var width))
                return width;
            width = Text.CalcSize(value).x;
            _smallElementWidths[value] = width;
            return width;
        }

        void CollectSelectableText(
            string text,
            float x,
            float y,
            List<TextRange> ranges,
            int sourceOffset
        )
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
                        int start = boundaries[i],
                            end = boundaries[i + 1];
                        for (int offset = start + 1; offset < end; offset++)
                            edges[offset] = edges[start];
                        edges[end] =
                            edges[start]
                            + SmallElementWidth(lineText.Substring(start, end - start));
                    }

                    var line = new TaskTextSelection.Line
                    {
                        Start = sourceOffset + range.Start,
                        End = sourceOffset + range.End,
                        X = x,
                        Y = lineY,
                        Width = edges[edges.Length - 1],
                        Height = lineH,
                        Text = lineText,
                        Boundaries = boundaries,
                    };
                    line.Edges.AddRange(edges);
                    _selectionLines.Add(line);
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
