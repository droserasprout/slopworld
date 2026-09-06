using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Appearance page for global UI scale, scheme, fonts, and cursor; font changes affect every
    // `Widgets.Label`/`Text.CalcSize`, including vanilla dialogs. ModOptions hosts the page.
    public class AppearancePage : IOptionPage
    {
        const float PickerWidth = 430f;
        const float PickerHeight = 360f;

        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly SmoothScroll _pickScroll = new SmoothScroll();
        float _fieldsH;
        bool _pickingCursor;

        public void Load() { }

        // The scale under the hand, while the hand is on it. Null when nothing is dragging.
        float? _scaleHeld;

        static ModSettings S => ModEntry.Instance.settings;

        public void Draw(Rect rect)
        {
            using (WidgetState.Save()) DrawCore(rect);
        }

        void DrawCore(Rect rect)
        {
            Text.Font = GameFont.Small;
            var body = UiWidgets.PageBody(rect);
            body.height += UiWidgets.BtnH + UiWidgets.GapS;
            var inner = body.ContractedBy(UiWidgets.GapM);

            // The preview sits at the foot; the form scrolls above it.
            float ph = Mathf.Clamp(
                UiWidgets.LineHOf(GameFont.Tiny) + UiWidgets.LineHOf(GameFont.Small)
                    + UiWidgets.LineHOf(GameFont.Medium) + UiWidgets.GapS * 5 + 36f,
                104f, 190f);
            var preview = new Rect(inner.x, inner.yMax - ph, inner.width, ph);
            var caption = new Rect(inner.x, preview.y - UiWidgets.RowH - UiWidgets.GapXS,
                inner.width, UiWidgets.RowH);

            var form = new Rect(inner.x, inner.y, inner.width,
                caption.y - inner.y - UiWidgets.GapS);
            var view = UiScrollBody.View(form, _fieldsH);
            using (_scroll.Scope(form, view))
                _fieldsH = DrawFields(view);

            // ---- preview
            UiWidgets.SectionHeading(caption, "Preview");
            DrawPreview(preview);

            if (_pickingCursor)
                DrawCursorPicker(rect);
        }

        float DrawFields(Rect rect)
        {
            float y = rect.y;
            y += DrawScale(new Rect(rect.x, y, rect.width, UiWidgets.ListingHeight));
            y += DrawInterface(new Rect(rect.x, y, rect.width, UiWidgets.ListingHeight));
            y += DrawScheme(new Rect(rect.x, y, rect.width, UiWidgets.ListingHeight));
            y += DrawFont(new Rect(rect.x, y, rect.width, UiWidgets.ListingHeight));
            y += DrawCursor(new Rect(rect.x, y, rect.width, UiWidgets.ListingHeight));
            return y - rect.y + UiWidgets.GapS;
        }

        float DrawScale(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            // The knob and the readout follow the hand; the scale itself is not moved until
            // the slider reports an actual mouse-up, because this is the one row whose value
            // decides where the row is drawn. See UiWidgets.Slider.
            float shown = _scaleHeld ?? UiScale.Current;
            float scale = UiWidgets.Slider(l, "UI scale", shown,
                UiScale.Min, UiScale.Max, UiScale.Readout(shown), out bool held,
                out bool released, "Zooms the whole interface, ours and the game's. Vanilla's own row stops "
                + "where the scaled screen would fall under 1024x768; this one does not.");
            if (held)
            {
                _scaleHeld = scale;
            }
            else if (released && _scaleHeld.HasValue)
            {
                _scaleHeld = null;
                UiScale.Set(scale);
            }
            UiScale.Flush();

            l.Gap(UiWidgets.GapM);
            bool fullscreen = UiWidgets.Checkbox(l, "Fullscreen", S.fullscreen,
                "Use window-manager fullscreen without changing Unity's render mode.");
            if (fullscreen != S.fullscreen) WindowMaximizer.Set(fullscreen);
            l.Gap(UiWidgets.GapM);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        float DrawInterface(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            bool disableTiny = UiWidgets.Checkbox(l, "DisableTinyText".Translate(),
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

            l.Gap(UiWidgets.GapM);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        float DrawScheme(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            // Nothing to invalidate on the way out: every color in the mod is read through
            // UiWidgets on the frame it is drawn, so the page under the dropdown has
            // already changed by the time the menu closes over it. See UIScheme.
            UiWidgets.Select(l, "Color scheme", UIScheme.Current.Label,
                UIScheme.All.Select(s => new SelectorOption(s.Label, () =>
                {
                    S.uiScheme = s.Id;
                    S.MarkDirty();
                })), out _);

            DrawSwatches(l.GetRect(18f));
            l.Gap(UiWidgets.GapM);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        float DrawFont(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            var fontOptions = new List<SelectorOption>
            {
                new SelectorOption("Automatic (system default)", () =>
                {
                    S.uiFontName = "";
                    UiFont.Apply();
                    S.MarkDirty();
                }),
            };
            fontOptions.AddRange(UiFont.All.Select(name => new SelectorOption(name, () =>
            {
                S.uiFontName = name;
                UiFont.Apply();
                S.MarkDirty();
            })));
            UiWidgets.Select(l, "Font", S.uiFontName.NullOrEmpty() ? "Automatic" : S.uiFontName,
                fontOptions, out _);

            l.Gap(UiWidgets.GapS);
            int size = Mathf.RoundToInt(UiWidgets.Slider(l, "Size", S.uiFontSize, 0, 24,
                S.uiFontSize > 0 ? $"{S.uiFontSize}pt" : "auto"));
            if (size != S.uiFontSize)
            {
                S.uiFontSize = size;
                UiFont.Apply();
                S.MarkDirty();
            }

            l.Gap(UiWidgets.GapXS);
            GUI.color = UiWidgets.Faint;
            l.Label(S.uiFontSize == 0
                ? "At 0pt the original per-tier sizes are kept (Tiny=11, Small=13, Medium=15); "
                    + "only the face changes."
                : "Custom size anchors Small; Tiny and Medium stay 2pt below and above it.");
            GUI.color = Color.white;

            l.Gap(UiWidgets.GapS);
            if (UiWidgets.Button(l, "Rescan installed fonts"))
                UiFont.Rescan();
            l.Gap(UiWidgets.GapM);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        float DrawCursor(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            CursorRow(l);
            l.Gap(UiWidgets.GapS);
            bool grayscale = UiWidgets.Checkbox(l, "Grayscale cursor", S.cursorGrayscale,
                "Use neutral grey instead of each asset's original colors.");
            if (grayscale != S.cursorGrayscale)
            {
                S.cursorGrayscale = grayscale;
                DeadCursor.Apply();
                S.MarkDirty();
            }
            l.Gap(UiWidgets.GapM);

            float used = l.CurHeight;
            l.End();
            return used;
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
            var row = l.GetRect(UiWidgets.RowH);
            float boxW = UiWidgets.RowH - 2f;
            float col = Mathf.Min(230f, row.width - boxW - UiWidgets.GapXS);

            UiWidgets.RowLabel(new Rect(row.x, row.y, col - UiWidgets.GapXS, row.height),
                "Mouse cursor");

            var box = new Rect(row.x + col, row.y + (row.height - boxW) / 2f, boxW, boxW);
            var choice = DeadCursor.ChoiceFor(DeadCursor.CurrentKey);
            var tex = choice != null ? DeadCursor.Preview(choice) : null;
            var tip = new TipSignal(DeadCursor.LabelFor(DeadCursor.CurrentKey) +
                ". Click to choose a cursor.", 0x51_0F_0100);
            if (IconPickerCell.DrawBox(box, r =>
            {
                if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, true);
            }, tip, RowHoverPolicy.Local))
                _pickingCursor = true;
        }

        // ----------------------------------------------------------------- picker

        void DrawCursorPicker(Rect pageRect)
        {
            UiPickerWindow.Show(0x51_0F_1100, pageRect, PickerWidth, PickerHeight, "Mouse cursor",
                () => _pickingCursor = false, DeadCursor.Choices.Length, _pickScroll,
                grid => DrawCursorGrid(grid));
        }

        void DrawCursorGrid(UiPickerWindow.Grid grid)
        {
            int count = DeadCursor.Choices.Length;
            for (int i = 0; i < count; i++)
            {
                int col = i % grid.Columns;
                int row = i / grid.Columns;
                var slot = new Rect(grid.View.x + col * grid.Cell,
                    grid.View.y + row * grid.Cell, grid.Cell, grid.Cell);
                var choice = DeadCursor.Choices[i];
                string key = choice.Key;

                RowChrome.Hover(slot, DeadCursor.CurrentKey == key, true,
                    RowHoverPolicy.Local);
                var tex = DeadCursor.Preview(choice);
                if (tex != null)
                    GUI.DrawTexture(new Rect(slot.x + (grid.Cell - grid.IconSize) / 2f,
                        slot.y + (grid.Cell - grid.IconSize) / 2f,
                        grid.IconSize, grid.IconSize),
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
            using (WidgetState.Save())
            {
                Slab.Box(r, UiWidgets.Well, UiWidgets.Edge);
                float x = r.x + 8f, y = r.y + 5f;
                float w = r.width - 16f;
                const float tagW = 52f;

                y = DrawTier(new Rect(x, y, w,
                        UiWidgets.LineHOf(GameFont.Medium) + 2f),
                    GameFont.Medium, "Medium", "Agents  ~/project  main", UiWidgets.Lead,
                    tagW);
                y += UiWidgets.GapXS;
                y = DrawTier(new Rect(x, y, w,
                        UiWidgets.LineHOf(GameFont.Small) + 2f),
                    GameFont.Small, "Small", "claude  working  +12 -3", UiWidgets.Name,
                    tagW);
                y += UiWidgets.GapXS;
                y = DrawTier(new Rect(x, y, w,
                        UiWidgets.LineHOf(GameFont.Tiny) + 2f),
                    GameFont.Tiny, "Tiny", "last output 14m ago  ·  metadata", UiWidgets.Dim,
                    tagW);
                y += UiWidgets.GapS;

                float gap = UiWidgets.GapXS;
                float chipW = (w - gap * 3f) / 4f;
                float chipH = Mathf.Min(22f, r.yMax - y - 5f);
                if (chipH > 0f)
                {
                    DrawColorKey(new Rect(x, y, chipW, chipH), "OK", UiWidgets.Yes);
                    DrawColorKey(new Rect(x + chipW + gap, y, chipW, chipH), "WARN",
                        UiWidgets.Warn);
                    DrawColorKey(new Rect(x + (chipW + gap) * 2f, y, chipW, chipH), "ERROR",
                        UiWidgets.Bad);
                    DrawColorKey(new Rect(x + (chipW + gap) * 3f, y, chipW, chipH), "LINK",
                        UiWidgets.Accent);
                }
            }
        }

        static float DrawTier(Rect r, GameFont font, string label, string sample, Color color,
            float tagW)
        {
            Slab.Fill(r, UiWidgets.RowBg);

            Text.Font = font;
            GUI.color = UiWidgets.Faint;
            UiWidgets.RowLabel(new Rect(r.x + 6f, r.y, tagW - 6f, r.height), label);

            GUI.color = color;
            UiWidgets.RowLabel(new Rect(r.x + tagW, r.y, r.width - tagW - 6f, r.height),
                sample);
            return r.yMax;
        }

        static void DrawColorKey(Rect r, string label, Color color)
        {
            Slab.Box(r, UiWidgets.RowBg, UiWidgets.Edge);
            Text.Font = GameFont.Tiny;
            GUI.color = color;
            UiWidgets.RowLabel(r.ContractedBy(2f), label, TextAnchor.MiddleCenter);
        }
    }
}
