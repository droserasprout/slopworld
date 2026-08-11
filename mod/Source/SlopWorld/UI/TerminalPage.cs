using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The pane's own settings, as a page of the options menu rather than a window of its
    // own. Font size and palette are the two things nobody can judge from a number - you
    // set them by looking at a terminal - so the preview is drawn live at the foot of the
    // page, same style, same glyphs, and every control invalidates the pane's style as it
    // moves so a pane underneath changes with it.
    //
    // A page rather than a Window because SlopOptions hangs it off an OptionCategoryDef,
    // the same way ConfigPage and UsagePage are. It owns no chrome and closes with the
    // dialog around it; the settings file is written once, on the dialog's close. See
    // SlopOptions.
    public class TerminalPage
    {
        readonly SmoothScroll _scroll = new SmoothScroll();
        // Last frame's measured height for the field column, for the scroll view.
        float _fieldsH;

        static SlopSettings S => SlopWorldMod.Instance.settings;

        public void Draw(Rect rect)
        {
            var s = S;
            Text.Font = GameFont.Small;

            SlopWidgets.PageCaption(rect, "The pane's look - font, palette and cursor.");

            // The one page of the four with no footer - nothing here is saved by a press, the
            // settings file is written when the dialog closes - so its body takes the bar's
            // room as well.
            var body = SlopWidgets.PageBody(rect);
            body.height += SlopWidgets.BtnH + SlopWidgets.GapS;
            SlopWidgets.Card(body);
            var inner = body.ContractedBy(SlopWidgets.GapM);

            // Taken first: the cell size the preview is laid out from is settled inside the
            // style's getter, and on the first frame there is no cell yet.
            var style = TerminalFont.Style;

            float ph = Mathf.Clamp(TerminalFont.CellH * PreviewRows + 10f, 70f, 190f);
            var preview = new Rect(inner.x, inner.yMax - ph, inner.width, ph);
            var caption = new Rect(inner.x, preview.y - SlopWidgets.RowH - SlopWidgets.GapXS,
                inner.width, SlopWidgets.RowH);

            // The fields scroll if the room is short; the preview stays put at the foot.
            var form = new Rect(inner.x, inner.y, inner.width,
                caption.y - inner.y - SlopWidgets.GapS);
            var view = new Rect(0f, 0f, form.width - SlopWidgets.ScrollbarW,
                Mathf.Max(_fieldsH, form.height));
            _scroll.Begin(form, view);

            // One column: a Listing_Standard given less height than its contents starts a
            // second column off the right edge rather than overflowing, which drops a text
            // field over the preview.
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(0f, 0f, view.width, 4000f));

            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH),
                    $"Font: {(s.fontName.NullOrEmpty() ? "Automatic" : s.fontName)}"))
            {
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption("Automatic", () =>
                    {
                        s.fontName = "";
                        TerminalFont.Invalidate();
                    }),
                };
                foreach (var name in TerminalFont.Mono)
                {
                    var picked = name;
                    opts.Add(new FloatMenuOption(picked, () =>
                    {
                        s.fontName = picked;
                        TerminalFont.Invalidate();
                    }));
                }
                Find.WindowStack.Add(new SlopMenu(opts));
            }

            l.Gap(SlopWidgets.GapM);
            int size = Mathf.RoundToInt(SlopWidgets.Slider(l, "Font size", s.fontSize,
                8, 28, s.fontSize.ToString()));
            if (size != s.fontSize)
            {
                s.fontSize = size;
                TerminalFont.Invalidate();
            }

            l.Gap(SlopWidgets.GapM);
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH), $"Colour scheme: {s.theme}"))
                Find.WindowStack.Add(new SlopMenu(TerminalTheme.All
                    .Select(t => new FloatMenuOption(t.Name, () =>
                    {
                        s.theme = t.Name;
                        TerminalTheme.Invalidate();
                    }))
                    .ToList()));

            DrawSwatches(l.GetRect(18f));

            l.Gap(SlopWidgets.GapM);
            l.Label("Cursor colour, #rrggbb (blank = the scheme's)");
            s.cursorColor = SlopWidgets.Field(l, "term.cursor", s.cursorColor ?? "");

            // Said rather than corrected: a half-typed "#8" is not a mistake yet, and a field
            // that rewrote itself under the cursor would be unusable.
            if (!string.IsNullOrEmpty(s.cursorColor) &&
                !TerminalTheme.TryHex(s.cursorColor, out _))
            {
                GUI.color = SlopWidgets.Bad;
                l.Label("Not a colour - the scheme's own cursor is being used.");
                GUI.color = Color.white;
            }

            _fieldsH = l.CurHeight + SlopWidgets.GapS;
            l.End();

            _scroll.End();

            // Re-taken: moving the slider invalidated the style a few lines up, so the one
            // from before it is a size out of date and the preview would sit a frame behind
            // the number over it.
            style = TerminalFont.Style;

            Text.Font = GameFont.Small;
            SlopWidgets.SectionHeading(caption, "Preview");

            DrawPreview(preview, style);
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
        const float PreviewPad = 5f;

        // Both settings at once, which is the whole reason they share a page: the size
        // and the palette are each half of what a pane looks like, and neither reads off a
        // slider. Drawn the way the pane draws - same style, same cell, same rule under a
        // link, same glyph put back over a block cursor - so what is judged here is what
        // arrives there.
        static void DrawPreview(Rect r, GUIStyle style)
        {
            var th = TerminalTheme.Current;
            Widgets.DrawBoxSolid(r, th.Bg);

            float cw = TerminalFont.CellW, ch = TerminalFont.CellH;
            if (cw <= 0f || ch <= 0f) return;

            int cols = Mathf.Max(1, Mathf.FloorToInt((r.width - PreviewPad * 2f) / cw));

            // Clipped, so a font too big for the box runs off its edge the way it would run
            // off the edge of a pane, rather than over the form above it.
            GUI.BeginClip(r);
            try
            {
                float y = 4f;

                int c = Run(style, "$ ", th.Ansi[10], 0, y, cols);
                Run(style, "claude --resume", th.Fg, c, y, cols);
                y += ch;

                Run(style, "  slopd -> installed", th.Ansi[6], 0, y, cols);
                y += ch;

                Run(style, "  error: no such file", th.Ansi[9], 0, y, cols);
                y += ch;

                c = Run(style, "  ", th.Fg, 0, y, cols);
                int end = Run(style, "https://example.com", th.Link, c, y, cols);
                var rule = th.Link;
                rule.a *= 0.5f;
                Widgets.DrawBoxSolid(
                    new Rect(PreviewPad + c * cw, y + ch - 1f, (end - c) * cw, 1f), rule);
                y += ch;

                // A block cursor is a reversed cell, so the box goes down opaque and the
                // character goes back over it. Exactly what DrawCursor does.
                c = Run(style, "$ ", th.Ansi[10], 0, y, cols);
                Run(style, "make", th.Fg, c, y, cols);
                if (c < cols)
                {
                    Widgets.DrawBoxSolid(
                        new Rect(PreviewPad + c * cw, y, cw, ch), TerminalTheme.CursorColor);
                    Run(style, "m", th.CursorText, c, y, cols);
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
        static int Run(GUIStyle style, string text, Color c, int col, float y, int cols)
        {
            if (col >= cols || string.IsNullOrEmpty(text)) return col;
            if (col + text.Length > cols) text = text.Substring(0, cols - col);

            style.normal.textColor = c;
            GUI.Label(
                new Rect(PreviewPad + col * TerminalFont.CellW, y,
                         text.Length * TerminalFont.CellW + 4f, TerminalFont.CellH),
                text, style);
            return col + text.Length;
        }
    }
}
