using System;
using System.Globalization;
using System.Text;

namespace SlopWorld
{
    // Materialize native line starts without rewriting rich tags or Unicode text elements.
    internal static class TooltipLineBreaks
    {
        internal static string Freeze(string text, Func<int, float> lineY, bool richText)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? "";
            var result = new StringBuilder(text.Length);
            int copied = 0,
                tagEnd = -1;
            float previousY = 0f;
            bool seenText = false,
                hardBreak = false;
            foreach (int offset in StringInfo.ParseCombiningCharacters(text))
            {
                if (offset <= tagEnd)
                    continue;
                if (richText && text[offset] == '<')
                {
                    int end = text.IndexOf('>', offset + 1);
                    string tag =
                        end < 0 ? "" : TagName(text.Substring(offset + 1, end - offset - 1));
                    if (IsFormattingTag(tag) || tag == "quad")
                    {
                        tagEnd = end;
                        if (tag != "quad")
                            continue;
                    }
                }
                if (text[offset] == '\r' || text[offset] == '\n')
                {
                    hardBreak = true;
                    continue;
                }
                float y = lineY(offset);
                if (seenText && !hardBreak && y > previousY + 0.5f)
                {
                    result.Append(text, copied, offset - copied).Append('\n');
                    copied = offset;
                }
                previousY = y;
                seenText = true;
                hardBreak = false;
            }
            return result.Append(text, copied, text.Length - copied).ToString();
        }

        static string TagName(string tag)
        {
            tag = tag.Trim().TrimStart('/');
            int end = 0;
            while (end < tag.Length && tag[end] != '=' && !char.IsWhiteSpace(tag[end]))
                end++;
            return tag.Substring(0, end).ToLowerInvariant();
        }

        static bool IsFormattingTag(string name) =>
            name == "b" || name == "i" || name == "color" || name == "size" || name == "material";
    }
}
