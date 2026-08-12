using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The non-terminal UI appearance settings, as a page of the options menu. Font face
    // and size are set here; they apply to every Widgets.Label and Text.CalcSize call in
    // the game — the mod's own views, the patched chrome (AgentSidebar, InspectPane), and
    // any vanilla RimWorld dialog still on screen. The hardware cursor is chosen here too,
    // beside the font controls because it is another thing the player reads on this screen.
    //
    // A page rather than a Window because SlopOptions hangs it off an OptionCategoryDef.
    // See SlopOptions.
    public class AppearancePage
    {
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly SmoothScroll _pickScroll = new SmoothScroll();
        float _fieldsH;
        bool _pickingCursor;

        static SlopSettings S => SlopWorldMod.Instance.settings;

        public void Draw(Rect rect)
        {
            Text.Font = GameFont.Small;
            SlopWidgets.PageCaption(rect,
                "The mod's look — font, size, and the pointer that follows your hand.");

            var body = SlopWidgets.PageBody(rect);
            body.height += SlopWidgets.BtnH + SlopWidgets.GapS;
            SlopWidgets.Card(body);
            var inner = body.ContractedBy(SlopWidgets.GapM);

            // The preview sits at the foot; the form scrolls above it.
            float ph = Mathf.Clamp(SlopWidgets.LineH * 4 + 20f, 60f, 160f);
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
                    }),
                };
                foreach (var name in SlopUIFont.All)
                {
                    var picked = name;
                    opts.Add(new FloatMenuOption(picked, () =>
                    {
                        S.uiFontName = picked;
                        SlopUIFont.Apply();
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

            l.Gap(SlopWidgets.GapS);
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH), "Rescan installed fonts"))
            {
                SlopUIFont.Rescan();
            }

            l.Gap(SlopWidgets.GapM);
            CursorRow(l);

            l.Gap(SlopWidgets.GapS);
            bool grayscale = SlopWidgets.Checkbox(l, "Grayscale cursor", S.cursorGrayscale,
                "Use neutral grey instead of each asset's original colours.");
            if (grayscale != S.cursorGrayscale)
            {
                S.cursorGrayscale = grayscale;
                DeadCursor.Apply();
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

        // A live preview of the current font at the current size, drawn in the mod's
        // own chrome colours so what is judged here is what arrives on the sidebar,
        // the top bar and the list views.
        static void DrawPreview(Rect r)
        {
            Slab.Box(r, SlopWidgets.Well, SlopWidgets.Edge);

            float x = r.x + 8f, y = r.y + 6f;
            float w = r.width - 16f;

            Text.Font = GameFont.Small;
            GUI.color = SlopWidgets.Lead;
            SlopWidgets.RowLabel(new Rect(x, y, w, SlopWidgets.LineH),
                "Agents  ~/project  main  +12 -3");
            y += SlopWidgets.LineH;

            GUI.color = SlopWidgets.Name;
            SlopWidgets.RowLabel(new Rect(x, y, w, SlopWidgets.LineH),
                "This is how your agent list will read.");
            y += SlopWidgets.LineH;

            GUI.color = SlopWidgets.Dim;
            SlopWidgets.RowLabel(new Rect(x, y, w, SlopWidgets.LineH),
                "A second line in the rung below it.");
            y += SlopWidgets.LineH;

            GUI.color = SlopWidgets.Faint;
            SlopWidgets.RowLabel(new Rect(x, y, w, SlopWidgets.LineH),
                "A fine print note. Quick brown fox.");
            GUI.color = Color.white;
        }
    }
}
