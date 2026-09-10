using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Terminal appearance page with a live preview; controls invalidate the pane theme and save
    // on dialog close. ModOptions hosts it as an `OptionCategoryDef` page.
    public class TerminalPage : IOptionPage
    {
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly SettingsContentHeight _height = new SettingsContentHeight(400f);
        readonly SettingsPreviewLayout _layout = new SettingsPreviewLayout();

        public void Load() { }

        static ModSettings S => ModEntry.Instance.settings;

        public void Draw(Rect rect)
        {
            using (WidgetState.Save()) DrawCore(rect);
        }

        void DrawCore(Rect rect)
        {
            Text.Font = GameFont.Small;
            var inner = SettingsPageLayout.Body(rect, false);
            var style = TerminalFont.Style;
            float ph = Mathf.Clamp(
                Mathf.Max(TerminalFont.CellH * PreviewRows + 10f, MatrixPreviewH),
                MatrixPreviewH, MatrixPreviewMaxH);
            float formH = _height.BeginFrame(Time.frameCount);
            float blockH = UiWidgets.RowH + UiWidgets.GapXS + ph;
            bool stacked = inner.height < UiWidgets.RowH + UiWidgets.GapM + blockH;
            float width = UiScrollBody.Measure(inner, 0f,
                UiScrollbarReservation.Always).ContentWidth;
            _layout.Arrange(stacked ? width : inner.width, inner.height, stacked, formH, ph, 0);
            if (stacked)
            {
                var geometry = UiScrollBody.Measure(inner, formH + UiWidgets.GapM + blockH,
                    UiScrollbarReservation.Always);
                using (_scroll.Scope(inner, geometry.View))
                {
                    _height.Measure(DrawFields(SettingsPageLayout.ToRect(_layout.Form), S));
                    DrawPreviewBlock(SettingsPageLayout.ToRect(_layout.PreviewCaption),
                        SettingsPageLayout.ToRect(_layout.Preview));
                }
            }
            else
            {
                var form = Place(inner, _layout.Form);
                var geometry = UiScrollBody.Measure(form, formH,
                    UiScrollbarReservation.Always);
                using (_scroll.Scope(form, geometry.View))
                    _height.Measure(DrawFields(geometry.View, S));
                DrawPreviewBlock(Place(inner, _layout.PreviewCaption), Place(inner, _layout.Preview));
            }
        }

        static Rect Place(Rect origin, UiLayoutRect local) =>
            new Rect(origin.x + local.X, origin.y + local.Y, local.Width, local.Height);

        static void DrawPreviewBlock(Rect caption, Rect preview)
        {
            Text.Font = GameFont.Small;
            UiWidgets.SectionHeading(caption, "Preview");
            DrawPreview(preview, TerminalFont.Style);
        }

        float DrawFields(Rect rect, ModSettings s)
        {
            float y = rect.y;
            y += DrawFont(new Rect(rect.x, y, rect.width, UiWidgets.ListingHeight), s);
            y += DrawTheme(new Rect(rect.x, y, rect.width, UiWidgets.ListingHeight), s);
            y += DrawCursor(new Rect(rect.x, y, rect.width, UiWidgets.ListingHeight), s);
            return y - rect.y + UiWidgets.GapS;
        }

        float DrawFont(Rect rect, ModSettings s)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            var fontOptions = new List<FloatMenuOption>
            {
                new FloatMenuOption("Automatic", () =>
                {
                    if (UiWidgets.SetSetting(s, ref s.fontName, "")) TerminalFont.Invalidate();
                }),
            };
            fontOptions.AddRange(UiWidgets.GroupedFontOptions(TerminalFont.Mono, name =>
            {
                if (UiWidgets.SetSetting(s, ref s.fontName, name)) TerminalFont.Invalidate();
            }));
            UiWidgets.Select(l, "Font", s.fontName.NullOrEmpty() ? "Automatic" : s.fontName,
                fontOptions, out _);

            if (UiWidgets.SliderSetting(l, "Font size", s, ref s.fontSize, 8, 28))
                TerminalFont.Invalidate();
            l.Gap(UiWidgets.GapM);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        float DrawTheme(Rect rect, ModSettings s)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            if (UiWidgets.Button(l,
                    $"Color scheme: {(s.theme == TerminalTheme.MatchUI ? "Match UI" : TerminalTheme.Current.Label)}"))
                Find.WindowStack.Add(new UiMenu(new[]
                    {
                        new FloatMenuOption("Match UI", () =>
                        {
                            if (UiWidgets.SetSetting(s, ref s.theme, TerminalTheme.MatchUI))
                                TerminalTheme.Invalidate();
                        }),
                    }.Concat(TerminalTheme.All
                    .Select(t => new FloatMenuOption(t.Label, () =>
                    {
                        if (UiWidgets.SetSetting(s, ref s.theme, t.Name)) TerminalTheme.Invalidate();
                    })))
                    .ToList()));

            DrawSwatches(l.GetRect(18f));
            l.Gap(UiWidgets.GapM);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        float DrawCursor(Rect rect, ModSettings s)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            l.Label("Cursor color, #rrggbb (blank = the scheme's)");
            UiWidgets.SetSetting(s, ref s.cursorColor,
                UiWidgets.Field(l, "term.cursor", s.cursorColor ?? "", defaultValue: ""));

            // Said rather than corrected: a half-typed "#8" is not a mistake yet, and a field
            // that rewrote itself under the cursor would be unusable.
            if (!string.IsNullOrEmpty(s.cursorColor) &&
                !TerminalTheme.TryHex(s.cursorColor, out _))
            {
                GUI.color = UiWidgets.Bad;
                l.Label("Not a color - the scheme's own cursor is being used.");
                GUI.color = Color.white;
            }

            float used = l.CurHeight;
            l.End();
            return used;
        }

        // The scheme, drawn rather than described: sixteen ANSI slots over the background
        // they will be read on, then the cursor as it will be, override and all.
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

            var text = new Rect(r.x + PreviewPad + matrixSize + UiWidgets.GapM,
                r.y + 4f, r.width - PreviewPad * 2f - matrixSize - UiWidgets.GapM,
                r.height - 8f);
            DrawTextPreview(text, style, th);
        }

        // Columns are foreground colors, rows are background colors; the letter in each cell
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
                string label = i.ToString("X");
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

            // Clipped, so a font too big for the box runs off its edge the way it would run
            // off the edge of a pane, rather than over the form above it.
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
                Run(style, "make", th.Fg, c, y, cols, PreviewPad);
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

        // Draws one run at a column and answers the column after it, so a line reads as the
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
