using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Appearance page for global UI scale, scheme, fonts, and cursor. Font changes affect every
    // `Widgets.Label`/`Text.CalcSize`, including vanilla dialogs. ModOptions hosts the page.
    public class AppearancePage : IOptionPage
    {
        const float PickerWidth = 430f;
        const float PickerHeight = 360f;

        readonly SmoothScroll _pickScroll = new SmoothScroll();
        readonly SettingsPreviewForm _form = new SettingsPreviewForm(600f,
            UiScrollbarReservation.WhenNeeded);
        bool _pickingCursor;
        int _contentRevision;

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
            _form.Draw(SettingsPageLayout.BodyWithoutFooter(rect), PreviewHeight(),
                DrawFields, DrawPreviewBlock, _contentRevision);

            if (_pickingCursor)
                DrawCursorPicker(rect);
        }

        static float PreviewHeight() => Mathf.Clamp(
            UiTheme.LineHOf(GameFont.Tiny) + UiTheme.LineHOf(GameFont.Small)
                + UiTheme.LineHOf(GameFont.Medium) + UiTheme.GapS * 8 + 90f,
            200f, 230f);

        static void DrawPreviewBlock(Rect caption, Rect preview)
        {
            UiLayout.SectionHeading(caption, "Preview");
            DrawPreview(preview);
        }

        void DrawFields(Listing_Standard l)
        {
            DrawScale(l);
            DrawFont(l);
            DrawScheme(l);
            DrawCursor(l);
        }

        void DrawScale(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Layout");

            // The knob and readout follow the mouse. Apply the scale only after mouse release.
            // The scale changes this row's position. See UiControls.Slider.
            float shown = _scaleHeld ?? UiScale.Current;
            float scale = UiControls.Slider(l, "UI scale", shown,
                UiScale.Min, UiScale.Max, UiScale.Readout(shown), out bool held,
                out bool released, "Scales the SlopWorld and game interfaces. The game's scale control stops "
                + "when the scaled screen would be smaller than 1024x768. This control has no such limit.");
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
        }

        void DrawScheme(Listing_Standard l)
        {
            // Nothing to invalidate on the way out: every color in the mod is read through shared
            // UI chrome on the frame it is drawn. Therefore, the page under the dropdown has
            // already changed by the time the menu closes over it. See UIScheme.
            UiControls.Select(l, "Color scheme", UIScheme.Current.Label,
                UIScheme.All.Select(s => new SelectorOption(s.Label, () =>
                {
                    S.uiScheme = s.Id;
                    S.MarkDirty();
                })), out _);

            DrawSwatches(l.GetRect(18f));
            l.Gap(UiTheme.GapM);

        }

        List<FloatMenuOption> _fontOptions;

        void DrawFont(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Font");
            // OS catalogs are stable for the page lifetime; group their faces once.
            if (_fontOptions == null)
            {
                _fontOptions = new List<FloatMenuOption>
                {
                    new FloatMenuOption("Automatic", () =>
                    {
                        S.uiFontName = "";
                        UiFont.Apply();
                        S.MarkDirty();
                        _contentRevision++;
                    }),
                };
                _fontOptions.AddRange(UiLayout.GroupedFontOptions(UiFont.All, name =>
                {
                    S.uiFontName = name;
                    UiFont.Apply();
                    S.MarkDirty();
                    _contentRevision++;
                }));
            }
            UiControls.Select(l, "Font", S.uiFontName.NullOrEmpty() ? "Automatic" : S.uiFontName,
                _fontOptions, out _);

            int size = Mathf.RoundToInt(UiControls.Slider(l, "Size", S.uiFontSize, 0, 24,
                S.uiFontSize > 0 ? $"{S.uiFontSize}pt" : "auto"));
            if (size != S.uiFontSize)
            {
                S.uiFontSize = size;
                UiFont.Apply();
                S.MarkDirty();
                _contentRevision++;
            }

            if (S.uiFontSize == 0)
            {
                GUI.color = UiTheme.Faint;
                l.Label("At 0pt, sizes remain Tiny=11, Small=13, and Medium=15. "
                    + "Only the typeface changes.");
                GUI.color = Color.white;
            }

            bool disableTiny = UiControls.Checkbox(l, "DisableTinyText".Translate(),
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

            l.Gap(UiTheme.GapS);
            if (UiLayout.Button(l, "Rescan installed fonts"))
            {
                UiFont.Rescan();
                _fontOptions = null;
                _contentRevision++;
            }
            l.Gap(UiTheme.GapM);

        }

        void DrawCursor(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Mouse");
            CursorRow(l);
            l.Gap(UiTheme.GapS);
            bool grayscale = UiControls.Checkbox(l, "Grayscale", S.cursorGrayscale,
                "Use neutral grey instead of each asset's original colors.");
            if (grayscale != S.cursorGrayscale)
            {
                S.cursorGrayscale = grayscale;
                DeadCursor.Apply();
                S.MarkDirty();
            }
            l.Gap(UiTheme.GapM);

        }

        // The scheme, drawn rather than described - the Terminal page's swatch strip, over the
        // surface these will actually be read on rather than over the page. The washes among them
        // are the point. A text ramp is five strengths of one color. The only way to see whether
        // the fifth is still a color is to lay it on its own well.
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
            var row = l.GetRect(UiTheme.RowH);
            float boxW = UiTheme.RowH - 2f;
            float col = Mathf.Min(230f, row.width - boxW - UiTheme.GapXS);

            UiText.RowLabel(new Rect(row.x, row.y, col - UiTheme.GapXS, row.height),
                "Cursor");

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
                if (UiButtons.RowButton(slot))
                {
                    DeadCursor.Choose(key);
                    _pickingCursor = false;
                }
            }
        }

        // A live preview of the UI tiers, controls, list chrome and semantic colors, drawn
        // over the same surfaces used by the sidebar, top bar and list views. This makes a
        // face or size choice legible even when the current page happens to use only Small.
        static void DrawPreview(Rect r)
        {
            using (WidgetState.Save())
            {
                Slab.Box(r, UiTheme.Well, UiTheme.Edge);
                float x = r.x + 8f, y = r.y + 5f;
                float w = r.width - 16f;
                const float tagW = 52f;

                y = DrawTier(new Rect(x, y, w,
                        UiTheme.LineHOf(GameFont.Medium) + 2f),
                    GameFont.Medium, "Medium", "Agents  ~/project  main", UiTheme.Lead,
                    tagW);
                y += UiTheme.GapXS;
                y = DrawTier(new Rect(x, y, w,
                        UiTheme.LineHOf(GameFont.Small) + 2f),
                    GameFont.Small, "Small", "claude  working  +12 -3", UiTheme.Name,
                    tagW);
                y += UiTheme.GapXS;
                y = DrawTier(new Rect(x, y, w,
                        UiTheme.LineHOf(GameFont.Tiny) + 2f),
                    GameFont.Tiny, "Tiny", "last output 14m ago  ·  metadata", UiTheme.Dim,
                    tagW);
                y += UiTheme.GapS;

                float gap = UiTheme.GapXS;
                float buttonH = Mathf.Min(24f, r.yMax - y - 5f);
                if (buttonH > 0f)
                {
                    float buttonW = (w - gap * 2f) / 3f;
                    DrawButtonExample(new Rect(x, y, buttonW, buttonH), "OPEN",
                        UiTheme.Accent, UIScheme.Current.AccentText, Color.clear);
                    DrawButtonExample(new Rect(x + buttonW + gap, y, buttonW, buttonH),
                        "DEFAULT", UiTheme.Well, UiTheme.Lead, UiTheme.Edge);
                    DrawButtonExample(new Rect(x + (buttonW + gap) * 2f, y, buttonW, buttonH),
                        "DELETE", UiTheme.Destructive, UIScheme.Current.DestructiveText,
                        Color.clear);
                    y += buttonH;
                }
                y += UiTheme.GapS;

                float controlH = Mathf.Min(24f, r.yMax - y - 5f);
                if (controlH > 0f)
                {
                    float controlW = (w - gap) / 2f;
                    DrawFieldExample(new Rect(x, y, controlW, controlH));
                    DrawCheckExample(new Rect(x + controlW + gap, y, controlW, controlH));
                    y += controlH;
                }
                y += UiTheme.GapS;

                float rowH = Mathf.Min(UiTheme.RowH, r.yMax - y - 5f);
                if (rowH > 0f)
                {
                    DrawListExample(new Rect(x, y, w, rowH));
                    y += rowH;
                }
                y += UiTheme.GapS;

                float chipW = (w - gap * 3f) / 4f;
                float chipH = Mathf.Min(22f, r.yMax - y - 5f);
                if (chipH > 0f)
                {
                    DrawColorKey(new Rect(x, y, chipW, chipH), "OK", UiTheme.Yes);
                    DrawColorKey(new Rect(x + chipW + gap, y, chipW, chipH), "WARN",
                        UiTheme.Warn);
                    DrawColorKey(new Rect(x + (chipW + gap) * 2f, y, chipW, chipH), "ERROR",
                        UiTheme.Bad);
                    DrawColorKey(new Rect(x + (chipW + gap) * 3f, y, chipW, chipH), "LINK",
                        UiTheme.Accent);
                }
            }
        }

        static void DrawButtonExample(Rect r, string label, Color face, Color text, Color edge)
        {
            Slab.Box(r, face, edge);
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Tiny;
                GUI.color = text;
                UiText.RowLabel(r.ContractedBy(2f), label, TextAnchor.MiddleCenter);
            }
        }

        static void DrawFieldExample(Rect r)
        {
            Slab.Box(r, UiTheme.Well, UiTheme.Edge);
            float labelW = Mathf.Min(54f, r.width * 0.32f);
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Tiny;
                GUI.color = UiTheme.Faint;
                UiText.RowLabel(new Rect(r.x + 6f, r.y, labelW, r.height), "command");
                Text.Font = GameFont.Small;
                GUI.color = UiTheme.Name;
                UiText.RowLabel(new Rect(r.x + labelW + 4f, r.y,
                    r.width - labelW - 10f, r.height), "make test");
            }
        }

        static void DrawCheckExample(Rect r)
        {
            Slab.Box(r, UiTheme.RowBg, UiTheme.Edge);
            var box = UiControls.TickBox(new Rect(r.x + 6f, r.y, UiControls.TickW, r.height), true);
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                GUI.color = UiTheme.Name;
                UiText.RowLabel(new Rect(box.xMax + UiTheme.GapS, r.y,
                    r.xMax - box.xMax - UiTheme.GapS - 4f, r.height), "Enabled");
            }
        }

        static void DrawListExample(Rect r)
        {
            Slab.Box(r, UiTheme.RowOn, UiTheme.EdgeLit);
            float marker = Mathf.Min(UiTheme.StatusMarker, r.height - 8f);
            Slab.Fill(new Rect(r.x + 6f, r.y + (r.height - marker) / 2f, marker, marker),
                UiTheme.StateWorking);
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                GUI.color = UiTheme.Name;
                UiText.RowLabel(new Rect(r.x + marker + 14f, r.y,
                    r.width * 0.52f, r.height), "agent terminal");
                Text.Font = GameFont.Tiny;
                GUI.color = UiTheme.StateWorking;
                UiText.RowLabel(new Rect(r.x + r.width * 0.52f, r.y,
                    r.width * 0.48f - 8f, r.height), "working", TextAnchor.MiddleRight);
            }
        }

        static float DrawTier(Rect r, GameFont font, string label, string sample, Color color,
            float tagW)
        {
            Slab.Fill(r, UiTheme.RowBg);

            Text.Font = font;
            GUI.color = UiTheme.Faint;
            UiText.RowLabel(new Rect(r.x + 6f, r.y, tagW - 6f, r.height), label);

            GUI.color = color;
            UiText.RowLabel(new Rect(r.x + tagW, r.y, r.width - tagW - 6f, r.height),
                sample);
            return r.yMax;
        }

        static void DrawColorKey(Rect r, string label, Color color)
        {
            Slab.Box(r, UiTheme.RowBg, UiTheme.Edge);
            Text.Font = GameFont.Tiny;
            GUI.color = color;
            UiText.RowLabel(r.ContractedBy(2f), label, TextAnchor.MiddleCenter);
        }
    }
}
