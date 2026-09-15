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
        public GUIStyle H5;
        public GUIStyle H6;
        readonly GUIStyle[] _headingCode = new GUIStyle[6];
        readonly Dictionary<GUIStyle, Dictionary<char, float>> _charWidths =
            new Dictionary<GUIStyle, Dictionary<char, float>>();

        public StyleSet()
        {
            Rebuild();
        }

        // Unity can replace the dynamic font or rebuild its atlas while a preview remains
        // open. Recreate styles and character measurements together so the next reflow uses
        // one typography snapshot for drawing, wrapping and hit testing.
        public void Rebuild()
        {
            _charWidths.Clear();
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
                H4 = Make(Text.CurFontStyle, FontStyle.Bold, -1);
                Text.Font = GameFont.Small;
                H5 = Make(Text.CurFontStyle, FontStyle.Bold, 0);
                H6 = Make(Text.CurFontStyle, FontStyle.Normal, 0);

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
                var headings = new[] { H1, H2, H3, H4, H5, H6 };
                for (int i = 0; i < headings.Length; i++)
                {
                    _headingCode[i] = new GUIStyle(Code)
                    {
                        fontSize = Size(headings[i]),
                        fontStyle = headings[i].fontStyle,
                    };
                }
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
            if (delta != 0) style.fontSize = Mathf.Max(1, Size(style) + delta);
            return style;
        }

        public GUIStyle For(InlineRun run, int heading)
        {
            if (run.Code) return heading >= 1 && heading <= 6 ? _headingCode[heading - 1] : Code;
            if (heading == 1) return H1;
            if (heading == 2) return H2;
            if (heading == 3) return H3;
            if (heading == 4) return H4;
            if (heading == 5) return H5;
            if (heading == 6) return H6;
            if (run.Bold && run.Italic) return BoldItalic;
            if (run.Bold) return Bold;
            if (run.Italic) return Italic;
            return Normal;
        }

        static int Size(GUIStyle style) => style.fontSize > 0
            ? style.fontSize : style.font != null ? Mathf.Max(1, style.font.fontSize) : 1;

        public static float Baseline(GUIStyle style)
        {
            var font = style.font;
            // Font ascent is expressed at its native size, including baked game fonts.
            return font != null && font.fontSize > 0
                ? Mathf.Max(0f, font.ascent * Size(style) / font.fontSize)
                : style.lineHeight;
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
