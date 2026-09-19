using System;
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
        readonly SettingsPreviewLayout _layout = new SettingsPreviewLayout();
        float _fieldsH;
        float _measuredFieldsH;
        int _measurementFrame = -1;
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
            // Listing measurement is produced while drawing. Promote it only at a frame
            // boundary so Layout, input and Repaint passes in one frame use identical bounds.
            if (_measurementFrame != Time.frameCount)
            {
                if (_measurementFrame >= 0 && _measuredFieldsH > 0f)
                    _fieldsH = _measuredFieldsH;
                _measurementFrame = Time.frameCount;
            }

            Text.Font = GameFont.Small;
            var inner = SettingsPageLayout.Body(rect, false);

            float previewH = PreviewHeight();
            float formH = _fieldsH > 0f ? _fieldsH : EstimateFieldsHeight();
            float blockH = UiTheme.RowH + UiTheme.GapXS + previewH;
            // Keep the preview pinned to the bottom while there is room for at least one
            // usable form row. The form owns its scrollbar; only genuinely short windows
            // move the preview into the shared scrolling column.
            float minimumFormViewport = UiTheme.RowH;
            bool stacked = inner.height < minimumFormViewport + UiTheme.GapM + blockH;

            if (!stacked)
            {
                _layout.Arrange(inner.width, inner.height, false, formH, previewH,
                    _contentRevision);
                var form = Place(inner, _layout.Form);
                var formGeometry = UiScrollBody.Measure(form, _fieldsH,
                    UiScrollbarReservation.WhenNeeded);
                using (_scroll.Scope(form, formGeometry.View))
                    DrawFields(new Rect(0f, 0f, formGeometry.View.width,
                        Mathf.Max(form.height, _fieldsH)));

                DrawPreviewBlock(Place(inner, _layout.PreviewCaption),
                    Place(inner, _layout.Preview));
            }
            else
            {
                // A short settings window becomes one scrollable column. The preview is
                // content, not a fixed overlay, so the last cursor/font setting remains
                // reachable even when the viewport is shorter than the form.
                float contentH = formH + UiTheme.GapM + blockH;
                var frame = inner;
                var geometry = UiScrollBody.Measure(frame, contentH,
                    UiScrollbarReservation.WhenNeeded);
                _layout.Arrange(geometry.View.width, inner.height, true, formH, previewH,
                    _contentRevision);
                using (_scroll.Scope(frame, geometry.View))
                {
                    DrawFields(new Rect(_layout.Form.X, _layout.Form.Y,
                        _layout.Form.Width, Mathf.Max(_layout.Form.Height, _fieldsH)));
                    DrawPreviewBlock(ToRect(_layout.PreviewCaption),
                        ToRect(_layout.Preview));
                }
            }

            if (_pickingCursor)
                DrawCursorPicker(rect);
        }

        static float PreviewHeight() => Mathf.Clamp(
            UiTheme.LineHOf(GameFont.Tiny) + UiTheme.LineHOf(GameFont.Small)
                + UiTheme.LineHOf(GameFont.Medium) + UiTheme.GapS * 8 + 90f,
            200f, 230f);

        // The first frame needs a safe content estimate before Listing_Standard has returned
        // the real height. It is replaced by `_fieldsH` at the next frame boundary and never
        // affects control identity or the page instance.
        static float EstimateFieldsHeight() => 900f;

        static Rect Place(Rect origin, UiLayoutRect local) =>
            new Rect(origin.x + local.X, origin.y + local.Y, local.Width, local.Height);

        static Rect ToRect(UiLayoutRect local) =>
            new Rect(local.X, local.Y, local.Width, local.Height);

        static void DrawPreviewBlock(Rect caption, Rect preview)
        {
            UiLayout.SectionHeading(caption, "Preview");
            DrawPreview(preview);
        }

        void DrawFields(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            bool begun = false;
            try
            {
                l.Begin(rect);
                begun = true;
                DrawScale(l);
                DrawDisplay(l);
                DrawFont(l);
                DrawScheme(l);
                DrawCursor(l);
                _measuredFieldsH = l.CurHeight - rect.y + UiTheme.GapS;
            }
            finally
            {
                if (begun) l.End();
            }
        }

        void DrawScale(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Layout");

            // The knob and the readout follow the hand; the scale itself is not moved until
            // the slider reports an actual mouse-up, because this is the one row whose value
            // decides where the row is drawn. See UiControls.Slider.
            float shown = _scaleHeld ?? UiScale.Current;
            float scale = UiControls.Slider(l, "UI scale", shown,
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

            bool fullscreen = UiControls.Checkbox(l, "Fullscreen", S.fullscreen,
                "Use window-manager fullscreen without changing Unity's render mode.");
            if (fullscreen != S.fullscreen) WindowMaximizer.Set(fullscreen);

            string density = UiDensityPreset.Normalize(S.uiDensity);
            UiControls.Select(l, "Density", UiDensityPreset.Label(density),
                new[]
                {
                    new SelectorOption("Default", () => SetSidebarLayout(ref S.uiDensity,
                        UiDensityPreset.Default)),
                    new SelectorOption("Compact", () => SetSidebarLayout(ref S.uiDensity,
                        UiDensityPreset.Compact)),
                }, out _);

            if (UiLayout.Button(l, "Reset sidebar layout", UiTheme.Btn.Ghost))
            {
                S.sidebarSide = NavigationSide.Left;
                S.uiDensity = UiDensityPreset.Default;
                S.sidebarHidden = false;
                S.sidebarWidth = WorkspaceLayout.DefaultNavigationWidth;
                S.MarkDirty();
                AgentSidebar.LayoutChanged();
            }
            UiLayout.Note(l, "Reset sidebar layout affects side, density, visibility, and width. "
                + "The sidebar width is still resized from its edge.");

        }

        static void SetSidebarLayout(ref string field, string value)
        {
            if (field == value) return;
            field = value;
            S.MarkDirty();
            AgentSidebar.LayoutChanged();
        }

        static void DrawDisplay(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Display");
            string mode = FramePolicy.Normalize(S.displayMode);
            int fps = FramePolicy.Clamp(S.foregroundFps);
            string framePacingLabel = mode == FramePolicy.Sync ? "VSync" : fps + " FPS";
            string framePacingTip = mode == FramePolicy.Limit
                ? "Disables VSync. Lower limits save power; higher limits improve responsiveness."
                : "VSync follows the display refresh rate for smooth presentation.";
            if (UiLayout.Button(l, "Frame pacing: " + framePacingLabel,
                    tip: framePacingTip))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("VSync", () =>
                    {
                        S.displayMode = FramePolicy.Sync;
                        S.MarkDirty();
                    })
                };
                options.AddRange(FramePolicy.Presets.Select(preset => new FloatMenuOption(
                    preset + " FPS", () =>
                    {
                        S.displayMode = FramePolicy.Limit;
                        S.foregroundFps = preset;
                        S.MarkDirty();
                    })));
                Find.WindowStack.Add(new UiMenu(options));
            }
        }

        void DrawScheme(Listing_Standard l)
        {
            // Nothing to invalidate on the way out: every color in the mod is read through
            // shared UI chrome on the frame it is drawn, so the page under the dropdown has
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

        void DrawFont(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Font");
            var fontOptions = new List<FloatMenuOption>
            {
                new FloatMenuOption("Automatic", () =>
                {
                    S.uiFontName = "";
                    UiFont.Apply();
                    S.MarkDirty();
                    _contentRevision++;
                }),
            };
            fontOptions.AddRange(UiLayout.GroupedFontOptions(UiFont.All, name =>
            {
                S.uiFontName = name;
                UiFont.Apply();
                S.MarkDirty();
                _contentRevision++;
            }));
            UiControls.Select(l, "Font", S.uiFontName.NullOrEmpty() ? "Automatic" : S.uiFontName,
                fontOptions, out _);

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
                l.Label("At 0pt the original per-tier sizes are kept (Tiny=11, Small=13, Medium=15); "
                    + "only the face changes.");
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
                _contentRevision++;
            }
            l.Gap(UiTheme.GapM);

        }

        void DrawCursor(Listing_Standard l)
        {
            CursorRow(l);
            l.Gap(UiTheme.GapS);
            bool grayscale = UiControls.Checkbox(l, "Grayscale cursor", S.cursorGrayscale,
                "Use neutral grey instead of each asset's original colors.");
            if (grayscale != S.cursorGrayscale)
            {
                S.cursorGrayscale = grayscale;
                DeadCursor.Apply();
                S.MarkDirty();
            }
            l.Gap(UiTheme.GapM);

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
            var row = l.GetRect(UiTheme.RowH);
            float boxW = UiTheme.RowH - 2f;
            float col = Mathf.Min(230f, row.width - boxW - UiTheme.GapXS);

            UiText.RowLabel(new Rect(row.x, row.y, col - UiTheme.GapXS, row.height),
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
