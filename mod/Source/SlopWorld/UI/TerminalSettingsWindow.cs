using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The pane's own settings, opened off the pane. Font size and palette are the two
    // things nobody can judge from a number - you set them by looking at a terminal, and
    // Options > Mod settings is behind a menu the terminal covers. So the gear is in the
    // title bar and this is what it opens.
    //
    // Everything here writes through as it moves: the pane under the window redraws in
    // the new scheme as the float menu closes. The file is written once, on the way out.
    public class TerminalSettingsWindow : Window
    {
        public static void Open()
        {
            if (Find.WindowStack == null) return;

            var open = Find.WindowStack.WindowOfType<TerminalSettingsWindow>();
            if (open != null) { open.Close(); return; }

            // Over the pane, or an ordinary dialog is added underneath a full-screen
            // terminal and never seen.
            TerminalWindow.OpenOverPane(new TerminalSettingsWindow());
        }

        public TerminalSettingsWindow()
        {
            doCloseX = true;
            draggable = true;
            absorbInputAroundWindow = true;
            onlyOneOfTypeAllowed = true;
            // Not close-on-click-outside: outside is the terminal, and a stray click that
            // shut the settings would also be a keystroke aimed at an agent.
            closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize => new Vector2(470f, 670f);

        static SlopSettings S => SlopWorldMod.Instance.settings;

        public override void PostClose()
        {
            base.PostClose();
            // Once, here, rather than on every drag of the slider: the pane has been
            // following the fields all along, and the file only has to agree by the time the
            // window is gone.
            S.Write();
        }

        public override void DoWindowContents(Rect rect)
        {
            var s = S;
            Text.Font = GameFont.Small;

            // Taken first: the cell size the preview is laid out from is settled inside the
            // style's getter, and on the first frame there is no cell yet.
            var style = TerminalFont.Style;

            float ph = Mathf.Clamp(TerminalFont.CellH * PreviewRows + 10f, 70f, 190f);
            var preview = new Rect(rect.x, rect.yMax - ph, rect.width, ph);
            var caption = new Rect(rect.x, preview.y - 24f, rect.width, 22f);

            // Begun on the room there is, and one column: a Listing_Standard given less
            // height than its contents starts a second column off the right edge rather than
            // overflowing, which drops the text field over the top of the form.
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(rect.x, rect.y, rect.width, caption.y - rect.y - 6f));

            if (l.ButtonText($"Font: {(s.fontName.NullOrEmpty() ? "Automatic" : s.fontName)}"))
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
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            l.Gap(8f);
            l.Label($"Font size: {s.fontSize}");
            int size = Mathf.RoundToInt(l.Slider(s.fontSize, 8, 28));
            if (size != s.fontSize)
            {
                s.fontSize = size;
                TerminalFont.Invalidate();
            }

            l.Gap(10f);
            bool wasSidebar = s.sidebar;
            l.CheckboxLabeled("Sidebar layout", ref s.sidebar,
                "Agents down the left, grouped by project, with a status line across the top. "
                + "Off, they are a row of portraits along the top instead.");
            // The pane under this window is still open, so it has to be moved by hand: the
            // postfix that shifts it runs on tab open and on resolution change only.
            if (s.sidebar != wasSidebar) Patch_MainTabWindowShift.Reposition();

            l.Gap(8f);
            if (l.ButtonText($"Colour scheme: {s.theme}"))
                Find.WindowStack.Add(new FloatMenu(TerminalTheme.All
                    .Select(t => new FloatMenuOption(t.Name, () =>
                    {
                        s.theme = t.Name;
                        TerminalTheme.Invalidate();
                    }))
                    .ToList()));

            DrawSwatches(l.GetRect(18f));

            l.Gap(10f);
            l.Label("Cursor colour, #rrggbb (blank = the scheme's)");
            s.cursorColor = l.TextEntry(s.cursorColor ?? "");

            // Said rather than corrected: a half-typed "#8" is not a mistake yet, and a field
            // that rewrote itself under the cursor would be unusable.
            if (!string.IsNullOrEmpty(s.cursorColor) &&
                !TerminalTheme.TryHex(s.cursorColor, out _))
            {
                GUI.color = new Color(0.95f, 0.45f, 0.45f);
                l.Label("Not a colour - the scheme's own cursor is being used.");
                GUI.color = Color.white;
            }

            l.End();

            // Re-taken: moving the slider invalidated the style a few lines up, so the one
            // from before it is a size out of date and the preview would sit a frame behind
            // the number over it.
            style = TerminalFont.Style;

            Text.Font = GameFont.Small;
            GUI.color = new Color(0.65f, 0.67f, 0.70f);
            Widgets.Label(caption, "Preview");
            GUI.color = Color.white;

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

        // Both settings at once, which is the whole reason they share a window: the size
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
