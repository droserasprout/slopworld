using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Appearance page for global UI scale, scheme, fonts, and cursor; font changes affect every
    // `Widgets.Label`/`Text.CalcSize`, including vanilla dialogs. SlopOptions hosts the page.
    public class AppearancePage : IOptionPage
    {
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly SmoothScroll _pickScroll = new SmoothScroll();
        float _fieldsH;
        bool _pickingCursor;

        public void Load() { }

        // The scale under the hand, while the hand is on it. Null when nothing is dragging.
        float? _scaleHeld;

        static SlopSettings S => SlopWorldMod.Instance.settings;

        public void Draw(Rect rect)
        {
            Text.Font = GameFont.Small;
            var body = SlopWidgets.PageBody(rect);
            body.height += SlopWidgets.BtnH + SlopWidgets.GapS;
            var inner = body.ContractedBy(SlopWidgets.GapM);

            // The preview sits at the foot; the form scrolls above it.
            float ph = Mathf.Clamp(
                SlopWidgets.LineHOf(GameFont.Tiny) + SlopWidgets.LineHOf(GameFont.Small)
                    + SlopWidgets.LineHOf(GameFont.Medium) + SlopWidgets.GapS * 5 + 36f,
                104f, 190f);
            var preview = new Rect(inner.x, inner.yMax - ph, inner.width, ph);
            var caption = new Rect(inner.x, preview.y - SlopWidgets.RowH - SlopWidgets.GapXS,
                inner.width, SlopWidgets.RowH);

            var form = new Rect(inner.x, inner.y, inner.width,
                caption.y - inner.y - SlopWidgets.GapS);
            var view = new Rect(0f, 0f, form.width - SlopWidgets.ScrollbarW,
                Mathf.Max(_fieldsH, form.height));
            using (_scroll.Scope(form, view))
                _fieldsH = DrawFields(view);

            // ---- preview
            SlopWidgets.SectionHeading(caption, "Preview");
            DrawPreview(preview);

            if (_pickingCursor)
                DrawCursorPicker(rect);
        }

        float DrawFields(Rect rect)
        {
            float y = rect.y;
            y += DrawScale(new Rect(rect.x, y, rect.width, 4000f));
            y += DrawInterface(new Rect(rect.x, y, rect.width, 4000f));
            y += DrawScheme(new Rect(rect.x, y, rect.width, 4000f));
            y += DrawFont(new Rect(rect.x, y, rect.width, 4000f));
            y += DrawCursor(new Rect(rect.x, y, rect.width, 4000f));
            y += DrawStatusbar(new Rect(rect.x, y, rect.width, 4000f));
            return y - rect.y + SlopWidgets.GapS;
        }

        float DrawScale(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            // The knob and the readout follow the hand; the scale itself is not moved until
            // the slider reports an actual mouse-up, because this is the one row whose value
            // decides where the row is drawn. See SlopWidgets.Slider.
            float shown = _scaleHeld ?? SlopUIScale.Current;
            float scale = SlopWidgets.Slider(l, "UI scale", shown,
                SlopUIScale.Min, SlopUIScale.Max, SlopUIScale.Readout(shown), out bool held,
                out bool released, "Zooms the whole interface, ours and the game's. Vanilla's own row stops "
                + "where the scaled screen would fall under 1024x768; this one does not.");
            if (held)
            {
                _scaleHeld = scale;
            }
            else if (released && _scaleHeld.HasValue)
            {
                _scaleHeld = null;
                SlopUIScale.Set(scale);
            }
            SlopUIScale.Flush();

            l.Gap(SlopWidgets.GapM);
            bool fullscreen = SlopWidgets.Checkbox(l, "Fullscreen", S.fullscreen,
                "Use window-manager fullscreen without changing Unity's render mode.");
            if (fullscreen != S.fullscreen) WindowMaximizer.Set(fullscreen);
            l.Gap(SlopWidgets.GapM);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        float DrawInterface(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            bool disableTiny = SlopWidgets.Checkbox(l, "DisableTinyText".Translate(),
                Prefs.DisableTinyText,
                "Use the Small font everywhere instead of the game's Tiny font.");
            if (disableTiny != Prefs.DisableTinyText)
            {
                Prefs.DisableTinyText = disableTiny;
                Widgets.ClearLabelCache();
                GenUI.ClearLabelWidthCache();
                if (Current.ProgramState == ProgramState.Playing)
                    Find.ColonistBar.drawer.ClearLabelCache();
            }

            l.Gap(SlopWidgets.GapM);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        float DrawScheme(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            // Nothing to invalidate on the way out: every color in the mod is read through
            // SlopWidgets on the frame it is drawn, so the page under the dropdown has
            // already changed by the time the menu closes over it. See UIScheme.
            if (SlopWidgets.Select(l, "Color scheme", UIScheme.Current.Label,
                    UIScheme.All.Select(s => s.Label), out var schemeBox))
                Find.WindowStack.Add(new SlopMenu(UIScheme.All
                    .Select(s => new FloatMenuOption(s.Label, () => { S.uiScheme = s.Id; S.MarkDirty(); }))
                    .ToList(), SlopWidgets.MenuAt(schemeBox)));

            DrawSwatches(l.GetRect(18f));
            l.Gap(SlopWidgets.GapM);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        float DrawFont(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            if (SlopWidgets.Select(l, "Font",
                    S.uiFontName.NullOrEmpty() ? "Automatic" : S.uiFontName,
                    new[] { "Automatic (system default)" }.Concat(SlopUIFont.All),
                    out var fontBox))
            {
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption("Automatic (system default)", () =>
                    {
                        S.uiFontName = "";
                        SlopUIFont.Apply();
                        S.MarkDirty();
                    }),
                };
                opts.AddRange(SlopWidgets.GroupedFontOptions(SlopUIFont.All, name =>
                {
                    S.uiFontName = name;
                    SlopUIFont.Apply();
                    S.MarkDirty();
                }));
                Find.WindowStack.Add(new SlopMenu(opts, SlopWidgets.MenuAt(fontBox)));
            }

            l.Gap(SlopWidgets.GapS);
            int size = Mathf.RoundToInt(SlopWidgets.Slider(l, "Size", S.uiFontSize, 0, 24,
                S.uiFontSize > 0 ? $"{S.uiFontSize}pt" : "auto"));
            if (size != S.uiFontSize)
            {
                S.uiFontSize = size;
                SlopUIFont.Apply();
                S.MarkDirty();
            }

            l.Gap(SlopWidgets.GapXS);
            GUI.color = SlopWidgets.Faint;
            l.Label(S.uiFontSize == 0
                ? "At 0pt the original per-tier sizes are kept (Tiny=11, Small=13, Medium=15); "
                    + "only the face changes."
                : "Custom size anchors Small; Tiny and Medium stay 2pt below and above it.");
            GUI.color = Color.white;

            l.Gap(SlopWidgets.GapS);
            if (SlopWidgets.Button(l, "Rescan installed fonts"))
                SlopUIFont.Rescan();
            l.Gap(SlopWidgets.GapM);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        float DrawCursor(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            CursorRow(l);
            l.Gap(SlopWidgets.GapS);
            bool grayscale = SlopWidgets.Checkbox(l, "Grayscale cursor", S.cursorGrayscale,
                "Use neutral grey instead of each asset's original colors.");
            if (grayscale != S.cursorGrayscale)
            {
                S.cursorGrayscale = grayscale;
                DeadCursor.Apply();
                S.MarkDirty();
            }
            l.Gap(SlopWidgets.GapM);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        float DrawStatusbar(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            SlopWidgets.SectionHeading(l, "Statusbar");
            bool u = SlopWidgets.Checkbox(l, "Show Usage in statusbar", S.statusbarUsage,
                "Show quota readouts in the top statusbar.");
            bool spent = SlopWidgets.Checkbox(l, "Show spent instead of left",
                Settings.UsageSpent,
                "Applies to every provider. Left is the amount remaining; spent is the " +
                "provider-facing percentage or amount used.");
            string clockPosition = StatusbarClockMode.Normalize(S.statusbarClockPosition);
            if (SlopWidgets.Select(l, "Clock position", StatusbarClockMode.Label(clockPosition),
                    new[] { "Right", "Center", "Hidden" }, out var clockBox))
                Find.WindowStack.Add(new SlopMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("Right", () => SetClockPosition(StatusbarClockMode.Right)),
                    new FloatMenuOption("Center", () => SetClockPosition(StatusbarClockMode.Center)),
                    new FloatMenuOption("Hidden", () => SetClockPosition(StatusbarClockMode.Hidden)),
                }, SlopWidgets.MenuAt(clockBox)));
            bool j = SlopWidgets.Checkbox(l, "Show Jukebox in statusbar", S.statusbarJukebox,
                "Show the jukebox door when a jukebox is present.");
            bool g = SlopWidgets.Checkbox(l, "Show GM in statusbar", S.statusbarGM,
                "Show the Computer Core door when the core is present.");
            bool indicators = SlopWidgets.Checkbox(l, "Show agent status indicators",
                S.statusbarAgentIndicators,
                "Show autostart, resume-on-start, and host-network flags in Agents.");
            if (u != S.statusbarUsage || spent != Settings.UsageSpent
                || j != S.statusbarJukebox || g != S.statusbarGM
                || indicators != S.statusbarAgentIndicators)
            {
                S.statusbarUsage = u;
                S.usageSpent = spent;
                S.statusbarJukebox = j;
                S.statusbarGM = g;
                S.statusbarAgentIndicators = indicators;
                S.MarkDirty();
            }

            float used = l.CurHeight;
            l.End();
            return used;
        }

        static void SetClockPosition(string position)
        {
            S.statusbarClockPosition = position;
            S.MarkDirty();
        }

        // The scheme, drawn rather than described - the Terminal page's swatch strip, over
        // the surface these will actually be read on rather than over the page. The washes
        // among them are the point: a text ramp is five strengths of one color, and the
        // only way to see whether the fifth is still a color is to lay it on its own well.
        static void DrawSwatches(Rect r)
        {
            var scheme = UIScheme.Current;
            Widgets.DrawBoxSolid(r, scheme.ViewBg);

            var sw = scheme.Swatches;
            float w = Mathf.Min(18f, (r.width - 8f) / sw.Length);
            for (int i = 0; i < sw.Length; i++)
                Widgets.DrawBoxSolid(
                    new Rect(r.x + 2f + i * w, r.y + 2f, w - 2f, r.height - 4f), sw[i]);
        }

        // Like the Usage page's icon rows, the current cursor is a small button at the
        // end of a named row. The choices live in a picker so the appearance page stays
        // readable while retaining the complete game-asset design pool.
        void CursorRow(Listing_Standard l)
        {
            var row = l.GetRect(SlopWidgets.RowH);
            float boxW = SlopWidgets.RowH - 2f;
            float col = Mathf.Min(230f, row.width - boxW - SlopWidgets.GapXS);

            SlopWidgets.RowLabel(new Rect(row.x, row.y, col - SlopWidgets.GapXS, row.height),
                "Mouse cursor");

            var box = new Rect(row.x + col, row.y + (row.height - boxW) / 2f, boxW, boxW);
            Slab.Box(box, SlopWidgets.Well, SlopWidgets.Edge);

            var choice = DeadCursor.ChoiceFor(DeadCursor.CurrentKey);
            var tex = choice != null ? DeadCursor.Preview(choice) : null;
            if (tex != null)
            {
                GUI.DrawTexture(box.ContractedBy(2f), tex, ScaleMode.ScaleToFit, true);
                GUI.color = Color.white;
            }

            RowChrome.Hover(box, false, true, RowHoverPolicy.Local);
            TooltipHandler.TipRegion(box, new TipSignal(
                DeadCursor.LabelFor(DeadCursor.CurrentKey) + ". Click to choose a cursor.",
                0x51_0F_0100));

            if (Widgets.ButtonInvisible(box)) _pickingCursor = true;
        }

        // ----------------------------------------------------------------- picker

        void DrawCursorPicker(Rect pageRect)
        {
            const float pickW = 430f;
            const float pickH = 360f;

            var pickRect = new Rect(
                pageRect.x + (pageRect.width - pickW) / 2f,
                pageRect.y + 50f,
                pickW, pickH);

            if (pickRect.yMax > pageRect.yMax - 8f)
                pickRect.y = pageRect.yMax - 8f - pickH;
            if (pickRect.y < pageRect.y + 8f)
                pickRect.y = pageRect.y + 8f;

            Find.WindowStack.ImmediateWindow(0x51_0F_1100, pickRect, WindowLayer.Super,
                () => DrawCursorPickerWindow(pickW, pickH), true, false, 1f);
        }

        void DrawCursorPickerWindow(float width, float height)
        {
            var r = new Rect(0f, 0f, width, height);
            Text.Font = GameFont.Small;
            SlopWidgets.RowLabel(
                new Rect(r.x + 8f, r.y + 4f, r.width - 60f, SlopWidgets.LineH),
                "Mouse cursor");

            if (SlopWidgets.Button(
                    new Rect(r.width - 48f, r.y + 2f, 44f, SlopWidgets.RowBtnH),
                    "X", SlopWidgets.Btn.Ghost))
                _pickingCursor = false;

            const float cell = 38f;
            int count = DeadCursor.Choices.Length;
            int perLine = Mathf.Max(1, Mathf.FloorToInt(
                (r.width - SlopWidgets.GapM) / cell));
            float gridTop = r.y + 4f + SlopWidgets.LineH + SlopWidgets.GapS;
            float gridH = r.height - gridTop - SlopWidgets.GapS;
            int rows = Mathf.CeilToInt(count / (float)perLine);
            float totalH = rows * cell;
            bool scroll = totalH > gridH;
            float gridW = scroll
                ? perLine * cell - SlopWidgets.ScrollbarW
                : perLine * cell;
            perLine = Mathf.Max(1, Mathf.FloorToInt(gridW / cell));
            rows = Mathf.CeilToInt(count / (float)perLine);
            totalH = rows * cell;

            var gridRect = new Rect(r.x + (r.width - gridW) / 2f, gridTop, gridW, gridH);
            var view = new Rect(0f, 0f, gridW, Mathf.Max(totalH, gridH));
            using (_pickScroll.Scope(gridRect, view))
                DrawCursorGrid(view, perLine, count, cell);
        }

        void DrawCursorGrid(Rect view, int perLine, int count, float cell)
        {
            const float iconSize = 30f;
            for (int i = 0; i < count; i++)
            {
                int col = i % perLine;
                int row = i / perLine;
                var slot = new Rect(view.x + col * cell, view.y + row * cell,
                    cell, cell);
                var choice = DeadCursor.Choices[i];
                string key = choice.Key;

                RowChrome.Hover(slot, DeadCursor.CurrentKey == key, true,
                    RowHoverPolicy.Local);
                var tex = DeadCursor.Preview(choice);
                if (tex != null)
                    GUI.DrawTexture(new Rect(slot.x + (cell - iconSize) / 2f,
                        slot.y + (cell - iconSize) / 2f, iconSize, iconSize),
                        tex, ScaleMode.ScaleToFit, true);

                TooltipHandler.TipRegion(slot, new TipSignal(
                    choice.Label + "\n" + choice.TexturePath,
                    0x51_0F_0120 ^ key.GetHashCode()));
                if (Widgets.ButtonInvisible(slot))
                {
                    DeadCursor.Choose(key);
                    _pickingCursor = false;
                }
            }
        }

        // A live preview of the three UI tiers and the semantic colors they carry, drawn
        // over the same surfaces used by the sidebar, top bar and list views. This makes a
        // face or size choice legible even when the current page happens to use only Small.
        static void DrawPreview(Rect r)
        {
            Slab.Box(r, SlopWidgets.Well, SlopWidgets.Edge);

            var wasFont = Text.Font;
            var wasColor = GUI.color;
            try
            {
                float x = r.x + 8f, y = r.y + 5f;
                float w = r.width - 16f;
                const float tagW = 52f;

                y = DrawTier(new Rect(x, y, w,
                        SlopWidgets.LineHOf(GameFont.Medium) + 2f),
                    GameFont.Medium, "Medium", "Agents  ~/project  main", SlopWidgets.Lead,
                    tagW);
                y += SlopWidgets.GapXS;
                y = DrawTier(new Rect(x, y, w,
                        SlopWidgets.LineHOf(GameFont.Small) + 2f),
                    GameFont.Small, "Small", "claude  working  +12 -3", SlopWidgets.Name,
                    tagW);
                y += SlopWidgets.GapXS;
                y = DrawTier(new Rect(x, y, w,
                        SlopWidgets.LineHOf(GameFont.Tiny) + 2f),
                    GameFont.Tiny, "Tiny", "last output 14m ago  ·  metadata", SlopWidgets.Dim,
                    tagW);
                y += SlopWidgets.GapS;

                float gap = SlopWidgets.GapXS;
                float chipW = (w - gap * 3f) / 4f;
                float chipH = Mathf.Min(22f, r.yMax - y - 5f);
                if (chipH > 0f)
                {
                    DrawColorKey(new Rect(x, y, chipW, chipH), "OK", SlopWidgets.Yes);
                    DrawColorKey(new Rect(x + chipW + gap, y, chipW, chipH), "WARN",
                        SlopWidgets.Warn);
                    DrawColorKey(new Rect(x + (chipW + gap) * 2f, y, chipW, chipH), "ERROR",
                        SlopWidgets.Bad);
                    DrawColorKey(new Rect(x + (chipW + gap) * 3f, y, chipW, chipH), "LINK",
                        SlopWidgets.Accent);
                }
            }
            finally
            {
                Text.Font = wasFont;
                GUI.color = wasColor;
            }
        }

        static float DrawTier(Rect r, GameFont font, string label, string sample, Color color,
            float tagW)
        {
            Slab.Fill(r, SlopWidgets.RowBg);

            Text.Font = font;
            GUI.color = SlopWidgets.Faint;
            SlopWidgets.RowLabel(new Rect(r.x + 6f, r.y, tagW - 6f, r.height), label);

            GUI.color = color;
            SlopWidgets.RowLabel(new Rect(r.x + tagW, r.y, r.width - tagW - 6f, r.height),
                sample);
            return r.yMax;
        }

        static void DrawColorKey(Rect r, string label, Color color)
        {
            Slab.Box(r, SlopWidgets.RowBg, SlopWidgets.Edge);
            Text.Font = GameFont.Tiny;
            GUI.color = color;
            SlopWidgets.RowLabel(r.ContractedBy(2f), label, TextAnchor.MiddleCenter);
        }
    }
}
