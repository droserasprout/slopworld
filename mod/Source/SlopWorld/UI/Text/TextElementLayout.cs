using System;
using System.Collections.Generic;
using System.Globalization;

namespace SlopWorld
{
    // Preserve UTF-16 source offsets while wrapping and hit-testing whole Unicode elements.
    // This shares StringInfo's element boundaries with InlineTextLayout and Markdown text.
    internal static class TextElementLayout
    {
        internal readonly struct Range
        {
            public readonly int Start, End;
            public Range(int start, int end) { Start = start; End = end; }
        }

        public static int[] Boundaries(string text)
        {
            var starts = StringInfo.ParseCombiningCharacters(text ?? "");
            var result = new int[starts.Length + 1];
            Array.Copy(starts, result, starts.Length);
            result[starts.Length] = (text ?? "").Length;
            return result;
        }

        public static List<Range> Wrap(string text, float width, Func<string, float> measure)
        {
            text = text ?? "";
            var boundaries = Boundaries(text);
            var ranges = new List<Range>();
            int start = 0, lastBreak = -1;
            float lineWidth = 0f;
            for (int i = 0; i < boundaries.Length - 1; i++)
            {
                int offset = boundaries[i], end = boundaries[i + 1];
                string element = text.Substring(offset, end - offset);
                if (element == "\n" || element == "\r\n")
                {
                    ranges.Add(new Range(boundaries[start], offset));
                    start = i + 1;
                    lastBreak = -1;
                    lineWidth = 0f;
                    continue;
                }
                float elementWidth = measure(element);
                if (lineWidth + elementWidth <= width || i == start)
                {
                    lineWidth += elementWidth;
                    if (char.IsWhiteSpace(text, offset)) lastBreak = i + 1;
                    continue;
                }
                int split = lastBreak > start ? lastBreak : i;
                ranges.Add(new Range(boundaries[start], boundaries[split]));
                start = split;
                lastBreak = -1;
                lineWidth = 0f;
                i = start - 1;
            }
            ranges.Add(new Range(boundaries[start], text.Length));
            return ranges;
        }
    }
}
