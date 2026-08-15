using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Appearance page for global UI scale, scheme, fonts, and cursor; font changes affect every
    // `Widgets.Label`/`Text.CalcSize`, including vanilla dialogs. SlopOptions hosts the page.
    public class AppearancePage
    {
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly SmoothScroll _pickScroll = new SmoothScroll();
        float _fieldsH;
        bool _pickingCursor;

        // The scale under the hand, while the hand is on it. Null when nothing is dragging.
        float? _scaleHeld;

        static SlopSettings S => SlopWorldMod.Instance.settings;

        public void Draw(Rect rect)
        {
            Text.Font = GameFont.Small;
            SlopWidgets.PageCaption(rect,
                "The mod's look — scale, colors, font, and the pointer that follows "
                + "your hand, plus what stays in the statusbar.");

            var body = SlopWidgets.PageBody(rect);
            body.height += SlopWidgets.BtnH + SlopWidgets.GapS;
            SlopWidgets.Card(body);
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
            _scroll.Begin(form, view);

            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(0f, 0f, view.width, 4000f));

            // ---- ui scale
            // The knob and the readout follow the hand; the scale itself is not moved until
            // the hand comes off, because this is the one row whose value decides where the
            // row is drawn. See SlopWidgets.Slider.
            float shown = _scaleHeld ?? SlopUIScale.Current;
            float scale = SlopWidgets.Slider(l, "UI scale", shown,
                SlopUIScale.Min, SlopUIScale.Max, SlopUIScale.Readout(shown), out bool held,
                "Zooms the whole interface, ours and the game's. Vanilla's own row stops "
                + "where the scaled screen would fall under 1024x768; this one does not.");
            if (held)
            {
                _scaleHeld = scale;
            }
            else if (_scaleHeld.HasValue)
            {
                _scaleHeld = null;
                SlopUIScale.Set(scale);
            }
            SlopUIScale.Flush();

            l.Gap(SlopWidgets.GapM);

            // ---- color scheme
            // Nothing to invalidate on the way out: every color in the mod is read through
            // SlopWidgets on the frame it is drawn, so the page under the dropdown has
            // already changed by the time the menu closes over it. See UIScheme.
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH),
                    $"Color scheme: {UIScheme.Current.Label}"))
                Find.WindowStack.Add(new SlopMenu(UIScheme.All
                    .Select(s => new FloatMenuOption(s.Label, () => { S.uiScheme = s.Id; S.MarkDirty(); }))
                    .ToList()));

            DrawSwatches(l.GetRect(18f));

            l.Gap(SlopWidgets.GapM);

            // ---- font face
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH),
                    $"Font: {(S.uiFontName.NullOrEmpty() ? "Automatic" : S.uiFontName)}"))
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
                foreach (var name in SlopUIFont.All)
                {
                    var picked = name;
                    opts.Add(new FloatMenuOption(picked, () =>
                    {
                        S.uiFontName = picked;
                        SlopUIFont.Apply();
                        S.MarkDirty();
                    }));
                }
                Find.WindowStack.Add(new SlopMenu(opts));
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

            // A note about size 0 meaning "keep the built-in per-tier sizes".
            if (S.uiFontSize == 0)
            {
                l.Gap(SlopWidgets.GapXS);
                GUI.color = SlopWidgets.Faint;
                l.Label("At 0pt the original per-tier sizes are kept (Tiny=11, " +
                        "Small=13, Medium=15); only the face changes.");
                GUI.color = Color.white;
            }
            else
            {
                l.Gap(SlopWidgets.GapXS);
                GUI.color = SlopWidgets.Faint;
                l.Label("Custom size anchors Small; Tiny and Medium stay 2pt below " +
                        "and above it.");
                GUI.color = Color.white;
            }

            l.Gap(SlopWidgets.GapS);
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH), "Rescan installed fonts"))
            {
                SlopUIFont.Rescan();
            }

            l.Gap(SlopWidgets.GapM);
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
            SlopWidgets.SectionHeading(l, "Statusbar");
            bool u = SlopWidgets.Checkbox(l, "Show Usage in statusbar", S.statusbarUsage,
                "Show quota readouts in the top statusbar.");
            string clockPosition = StatusbarClockMode.Normalize(S.statusbarClockPosition);
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH),
                    $"Clock position: {StatusbarClockMode.Label(clockPosition)}"))
            {
                Find.WindowStack.Add(new SlopMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("Right", () => SetClockPosition(StatusbarClockMode.Right)),
                    new FloatMenuOption("Center", () => SetClockPosition(StatusbarClockMode.Center)),
                    new FloatMenuOption("Hidden", () => SetClockPosition(StatusbarClockMode.Hidden)),
                }));
            }
            bool j = SlopWidgets.Checkbox(l, "Show Jukebox in statusbar", S.statusbarJukebox,
                "Show the jukebox door when a jukebox is present.");
            bool g = SlopWidgets.Checkbox(l, "Show GM in statusbar", S.statusbarGM,
                "Show the Computer Core door when the core is present.");
            if (u != S.statusbarUsage
                || j != S.statusbarJukebox || g != S.statusbarGM)
            {
                S.statusbarUsage = u;
                S.statusbarJukebox = j;
                S.statusbarGM = g;
                S.MarkDirty();
            }

            _fieldsH = l.CurHeight + SlopWidgets.GapS;
            l.End();

            _scroll.End();

            // ---- preview
            SlopWidgets.SectionHeading(caption, "Preview");
            DrawPreview(preview);

            if (_pickingCursor)
                DrawCursorPicker(rect);
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

            if (Mouse.IsOver(box)) Slab.Fill(box, SlopWidgets.Hover);
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
                () =>
                {
                    var r = new Rect(0f, 0f, pickW, pickH);
                    Text.Font = GameFont.Small;
                    SlopWidgets.RowLabel(
                        new Rect(r.x + 8f, r.y + 4f, r.width - 60f, SlopWidgets.LineH),
                        "Mouse cursor");

                    if (SlopWidgets.Button(
                            new Rect(r.width - 48f, r.y + 2f, 44f, SlopWidgets.RowBtnH),
                            "X", SlopWidgets.Btn.Ghost))
                    {
                        _pickingCursor = false;
                    }

                    const float Cell = 38f;
                    const float IconSize = 30f;
                    int perLine = Mathf.Max(1, Mathf.FloorToInt(
                        (r.width - SlopWidgets.GapM) / Cell));
                    int count = DeadCursor.Choices.Length;
                    float gridTop = r.y + 4f + SlopWidgets.LineH + SlopWidgets.GapS;
                    float gridH = r.height - gridTop - SlopWidgets.GapS;
                    int rows = Mathf.CeilToInt(count / (float)perLine);
                    float totalH = rows * Cell;
                    bool scroll = totalH > gridH;
                    float gridW2 = scroll
                        ? perLine * Cell - SlopWidgets.ScrollbarW
                        : perLine * Cell;
                    perLine = Mathf.Max(1, Mathf.FloorToInt(gridW2 / Cell));
                    rows = Mathf.CeilToInt(count / (float)perLine);
                    totalH = rows * Cell;

                    var gridRect = new Rect(r.x + (r.width - gridW2) / 2f, gridTop,
                        gridW2, gridH);
                    var view = new Rect(0f, 0f, gridW2, Mathf.Max(totalH, gridH));
                    _pickScroll.Begin(gridRect, view);

                    for (int i = 0; i < count; i++)
                    {
                        int col = i % perLine;
                        int row = i / perLine;
                        var cell = new Rect(view.x + col * Cell, view.y + row * Cell,
                            Cell, Cell);
                        var choice = DeadCursor.Choices[i];
                        string key = choice.Key;
                        bool selected = DeadCursor.CurrentKey == key;

                        if (selected) Slab.Fill(cell, SlopWidgets.RowOn);
                        if (Mouse.IsOver(cell)) Slab.Fill(cell, SlopWidgets.Hover);
                        var tex = DeadCursor.Preview(choice);
                        var icon = new Rect(cell.x + (Cell - IconSize) / 2f,
                            cell.y + (Cell - IconSize) / 2f, IconSize, IconSize);
                        if (tex != null)
                        {
                            GUI.DrawTexture(icon, tex, ScaleMode.ScaleToFit, true);
                        }

                        TooltipHandler.TipRegion(cell, new TipSignal(
                            choice.Label + "\n" + choice.TexturePath,
                            0x51_0F_0120 ^ key.GetHashCode()));
                        if (Widgets.ButtonInvisible(cell))
                        {
                            DeadCursor.Choose(key);
                            _pickingCursor = false;
                        }
                    }
                    _pickScroll.End();
                }, true, false, 1f);
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
