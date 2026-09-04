using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    sealed class StyleSet
    {
        public GUIStyle Normal;
        public GUIStyle Bold;
        public GUIStyle Italic;
        public GUIStyle BoldItalic;
        public GUIStyle Code;
        public GUIStyle H1;
        public GUIStyle H2;
        public GUIStyle H3;
        public GUIStyle H4;
        readonly Dictionary<GUIStyle, Dictionary<char, float>> _charWidths =
            new Dictionary<GUIStyle, Dictionary<char, float>>();

        public StyleSet()
        {
            var oldFont = Text.Font;
            try
            {
                Text.Font = GameFont.Small;
                Normal = Make(Text.CurFontStyle, FontStyle.Normal, 0);
                Bold = Make(Text.CurFontStyle, FontStyle.Bold, 0);
                Italic = Make(Text.CurFontStyle, FontStyle.Italic, 0);
                BoldItalic = Make(Text.CurFontStyle, FontStyle.BoldAndItalic, 0);

                Text.Font = GameFont.Medium;
                H1 = Make(Text.CurFontStyle, FontStyle.Bold, 2);
                H2 = Make(Text.CurFontStyle, FontStyle.Bold, 1);
                H3 = Make(Text.CurFontStyle, FontStyle.Bold, 0);
                H4 = Make(Text.CurFontStyle, FontStyle.Normal, 0);

                Code = new GUIStyle(TerminalFont.Style)
                {
                    alignment = TextAnchor.UpperLeft,
                    clipping = TextClipping.Overflow,
                    margin = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(0, 0, 0, 0),
                    richText = false,
                    wordWrap = false,
                };
                // TerminalWindow reuses TerminalFont.Style and mutates its normal text
                // color for every ANSI run. Do not inherit the last terminal foreground;
                // MarkdownRenderer applies the scheme color through GUI.color.
                Code.normal.textColor = Color.white;
            }
            finally
            {
                Text.Font = oldFont;
            }
        }

        static GUIStyle Make(GUIStyle source, FontStyle fontStyle, int delta)
        {
            var style = new GUIStyle(source)
            {
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Overflow,
                fontStyle = fontStyle,
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                richText = false,
                wordWrap = false,
            };
            if (style.fontSize > 0) style.fontSize += delta;
            return style;
        }

        public GUIStyle For(InlineRun run, int heading)
        {
            if (run.Code) return Code;
            if (heading == 1) return H1;
            if (heading == 2) return H2;
            if (heading == 3) return H3;
            if (heading == 4) return H4;
            if (run.Bold && run.Italic) return BoldItalic;
            if (run.Bold) return Bold;
            if (run.Italic) return Italic;
            return Normal;
        }

        public float MeasureChar(GUIStyle style, char value)
        {
            if (!_charWidths.TryGetValue(style, out var widths))
            {
                widths = new Dictionary<char, float>();
                _charWidths[style] = widths;
            }
            if (!widths.TryGetValue(value, out var width))
            {
                width = style.CalcSize(new GUIContent(value.ToString())).x;
                widths[value] = width;
            }
            return width;
        }
    }
}
