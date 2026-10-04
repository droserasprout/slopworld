using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Terminal appearance page with a live preview. Controls invalidate the pane theme and save
    // on dialog close. ModOptions hosts it as an `OptionCategoryDef` page.
    public class TerminalPage : IOptionPage
    {
        readonly SettingsPreviewForm _form = new SettingsPreviewForm(400f);

        public void Load() { }

        static ModSettings S => ModEntry.Instance.settings;

        public void Draw(Rect rect)
        {
            using (WidgetState.Save()) DrawCore(rect);
        }

        void DrawCore(Rect rect)
        {
            Text.Font = GameFont.Small;
            var inner = SettingsPageLayout.BodyWithoutFooter(rect);
            _ = TerminalFont.Style;
            float ph = Mathf.Clamp(
                Mathf.Max(TerminalFont.CellH * PreviewRows + 10f, MatrixPreviewH),
                MatrixPreviewH, MatrixPreviewMaxH);
            _form.Draw(inner, ph, DrawFields, DrawPreviewBlock);
        }

        static void DrawPreviewBlock(Rect caption, Rect preview)
        {
            Text.Font = GameFont.Small;
            UiLayout.SectionHeading(caption, "Preview");
            DrawPreview(preview, TerminalFont.Style);
        }

        void DrawFields(Listing_Standard l)
        {
            DrawFont(l, S);
            DrawTheme(l, S);
            DrawCursor(l, S);
        }

        List<FloatMenuOption> _fontOptions;

        void DrawFont(Listing_Standard l, ModSettings s)
        {
            UiLayout.SectionHeading(l, "Font");
            // OS catalogs are stable for the page lifetime; group their faces once.
            if (_fontOptions == null)
            {
                _fontOptions = new List<FloatMenuOption>
                {
                    new FloatMenuOption("Automatic", () =>
                    {
                        if (UiControls.SetSetting(s, ref s.fontName, "")) TerminalFont.Invalidate();
                    }),
                };
                _fontOptions.AddRange(UiLayout.GroupedFontOptions(TerminalFont.Mono, name =>
                {
                    if (UiControls.SetSetting(s, ref s.fontName, name)) TerminalFont.Invalidate();
                }));
            }
            UiControls.Select(l, "Font", s.fontName.NullOrEmpty() ? "Automatic" : s.fontName,
                _fontOptions, out _);

            if (UiControls.SliderSetting(l, "Font size", s, ref s.fontSize, 8, 28))
                TerminalFont.Invalidate();
            l.Gap(UiTheme.GapM);

        }

        void DrawTheme(Listing_Standard l, ModSettings s)
        {
            UiLayout.SectionHeading(l, "Colors");
            if (UiLayout.Button(l,
                    $"Color scheme: {(s.theme == TerminalTheme.MatchUI ? "Match UI" : TerminalTheme.Current.Label)}"))
                Find.WindowStack.Add(new UiMenu(new[]
                    {
                        new FloatMenuOption("Match UI", () =>
                        {
                            if (UiControls.SetSetting(s, ref s.theme, TerminalTheme.MatchUI))
                                TerminalTheme.Invalidate();
                        }),
                    }.Concat(TerminalTheme.All
                    .Select(t => new FloatMenuOption(t.Label, () =>
                    {
                        if (UiControls.SetSetting(s, ref s.theme, t.Name)) TerminalTheme.Invalidate();
                    })))
                    .ToList()));

            DrawSwatches(l.GetRect(18f));
            l.Gap(UiTheme.GapM);

        }

        void DrawCursor(Listing_Standard l, ModSettings s)
        {
            UiLayout.SectionHeading(l, "Terminal cursor");
            l.Label("Cursor color (#rrggbb, blank uses theme)");
            UiControls.SetSetting(s, ref s.cursorColor,
                UiControls.Field(l, "term.cursor", s.cursorColor ?? "", defaultValue: ""));

            // Said rather than corrected: a half-typed "#8" is not a mistake yet, and a field
            // that rewrote itself under the cursor would be unusable.
            if (!string.IsNullOrEmpty(s.cursorColor) &&
                (!HexColor.TryHex(s.cursorColor, out _) ||
                 s.cursorColor.Trim().TrimStart('#').Length != 6))
            {
                GUI.color = UiTheme.Bad;
                l.Label("Enter a valid #rrggbb color, or leave the field blank to use the theme cursor color.");
                GUI.color = Color.white;
            }

        }

        // The scheme, drawn rather than described. Sixteen ANSI slots over the background they will
        // be read on, then the cursor as it will be, override and all.
        static void DrawSwatches(Rect r)
        {
            var theme = TerminalTheme.Current;
            Widgets.DrawBoxSolid(r, theme.Bg);

            float w = Mathf.Min(18f, (r.width - 24f) / 17f);
            for (int i = 0; i < 16; i++)
                Widgets.DrawBoxSolid(
                    new Rect(r.x + 2f + i * w, r.y + 2f, w - 2f, r.height - 4f), theme.Ansi[i]);

            Widgets.DrawBoxSolid(
                new Rect(r.x + 6f + 16 * w, r.y + 2f, w - 2f, r.height - 4f),
                TerminalTheme.CursorColor);
        }

        // ----------------------------------------------------------------- preview

        const int PreviewRows = 5;
        const int AnsiColors = 16;
        const float MatrixPreviewH = 190f;
        const float MatrixPreviewMaxH = 220f;
        const float PreviewPad = 5f;

        // Preview the complete ANSI foreground/background matrix beside a few real terminal
        // lines. The matrix uses the same 16 slots the pane resolves for SGR colors.
        static void DrawPreview(Rect r, GUIStyle style)
        {
            var th = TerminalTheme.Current;
            Widgets.DrawBoxSolid(r, th.Bg);
            if (r.width < 280f)
            {
                DrawTextPreview(r, style, th);
                return;
            }

            float cell = Mathf.Min(12f, (r.height - 8f) / (AnsiColors + 1f));
            cell = Mathf.Min(cell, (r.width - 160f) / (AnsiColors + 1f));
            cell = Mathf.Max(6f, cell);
            float matrixSize = cell * (AnsiColors + 1f);

            DrawAnsiMatrix(new Rect(r.x + PreviewPad, r.y + 4f, matrixSize, matrixSize),
                th, style, cell);

            var text = new Rect(r.x + PreviewPad + matrixSize + UiTheme.GapM,
                r.y + 4f, r.width - PreviewPad * 2f - matrixSize - UiTheme.GapM,
                r.height - 8f);
            DrawTextPreview(text, style, th);
        }

        // Columns are foreground colors, rows are background colors. The letter in each cell
        // makes every pair visible even when the colors themselves are close.
        static void DrawAnsiMatrix(Rect r, TerminalTheme th, GUIStyle source, float cell)
        {
            var style = new GUIStyle(source)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                fontSize = Mathf.Max(8, Mathf.FloorToInt(cell * 0.8f)),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };

            GUI.color = Color.white;
            style.normal.textColor = th.Fg;
            for (int i = 0; i < AnsiColors; i++)
            {
                string label = i.ToString("X", CultureInfo.InvariantCulture);
                GUI.Label(new Rect(r.x + (i + 1) * cell, r.y, cell, cell), label, style);
                GUI.Label(new Rect(r.x, r.y + (i + 1) * cell, cell, cell), label, style);
            }

            for (int bg = 0; bg < AnsiColors; bg++)
            {
                for (int fg = 0; fg < AnsiColors; fg++)
                {
                    var box = new Rect(r.x + (fg + 1) * cell, r.y + (bg + 1) * cell,
                        cell - 1f, cell - 1f);
                    Widgets.DrawBoxSolid(box, th.Ansi[bg]);
                    style.normal.textColor = th.Ansi[fg];
                    GUI.Label(box, "A", style);
                }
            }
        }

        static void DrawTextPreview(Rect r, GUIStyle style, TerminalTheme th)
        {
            float cw = TerminalFont.CellWAtScreenScale(Prefs.UIScale), ch = TerminalFont.CellH;
            if (cw <= 0f || ch <= 0f) return;
            GUI.color = Color.white;

            int cols = Mathf.Max(1, Mathf.FloorToInt((r.width - PreviewPad * 2f) / cw));

            // Clip the preview to its box.
            // An oversized font then stops at the box edge instead of drawing over the form.
            GUI.BeginClip(r);
            try
            {
                float y = 4f;

                int c = Run(style, "$ ", th.Ansi[10], 0, y, cols, PreviewPad);
                Run(style, "claude --resume", th.Fg, c, y, cols, PreviewPad);
                y += ch;

                Run(style, "  slopd -> installed", th.Ansi[6], 0, y, cols, PreviewPad);
                y += ch;

                Run(style, "  error: no such file", th.Ansi[9], 0, y, cols, PreviewPad);
                y += ch;

                c = Run(style, "  ", th.Fg, 0, y, cols, PreviewPad);
                int end = Run(style, "https://example.com", th.Link, c, y, cols, PreviewPad);
                var rule = th.Link;
                rule.a *= 0.5f;
                Widgets.DrawBoxSolid(
                    new Rect(PreviewPad + c * cw, y + ch - 1f, (end - c) * cw, 1f), rule);
                y += ch;

                // A block cursor is a reversed cell, so the box goes down opaque and the
                // character goes back over it. Exactly what DrawCursor does.
                c = Run(style, "$ ", th.Ansi[10], 0, y, cols, PreviewPad);
                Run(style, "just", th.Fg, c, y, cols, PreviewPad);
                if (c < cols)
                {
                    Widgets.DrawBoxSolid(
                        new Rect(PreviewPad + c * cw, y, cw, ch), TerminalTheme.CursorColor);
                    Run(style, "m", th.CursorText, c, y, cols, PreviewPad);
                }
            }
            finally
            {
                GUI.EndClip();
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
        }

        // Draws one run at a column and answers the column after it. Therefore, a line reads as the
        // segments it is made of rather than as arithmetic.
        static int Run(GUIStyle style, string text, Color c, int col, float y, int cols,
                       float pad)
        {
            if (col >= cols || string.IsNullOrEmpty(text)) return col;
            if (col + text.Length > cols) text = text.Substring(0, cols - col);

            style.normal.textColor = c;
            float cw = TerminalFont.CellWAtScreenScale(Prefs.UIScale);
            GUI.Label(
                new Rect(pad + col * cw, y, text.Length * cw + 4f,
                         TerminalFont.CellH),
                text, style);
            return col + text.Length;
        }
    }
}
