using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public abstract class UiText : UiTheme
    {
        public static float Paragraph(Rect r, float y, string text, GameFont font, Color color,
            TextAnchor anchor, int textSize = 0)
        {
            if (textSize > 0)
            {
                var style = SizedStyle(font, textSize, anchor, true);
                float largeH = Mathf.Max(1f,
                    style.CalcHeight(new GUIContent(text ?? ""), r.width)) +
                    GapXS;
                var largeColor = GUI.color;
                GUI.color = color;
                GUI.Label(new Rect(r.x, y, r.width, largeH), text ?? "", style);
                GUI.color = largeColor;
                return y + largeH;
            }

            var wasFont = Verse.Text.Font;
            var wasColor = GUI.color;
            var wasWrap = Verse.Text.WordWrap;
            var wasAnchor = Verse.Text.Anchor;
            Verse.Text.Font = font;
            Verse.Text.WordWrap = true;
            Verse.Text.Anchor = anchor;
            float h = UiText.PlainStatusLabelHeight(text, r.width, font);
            UiText.PlainStatusLabel(new Rect(r.x, y, r.width, h), text, color, font, anchor);
            Verse.Text.Anchor = wasAnchor;
            Verse.Text.WordWrap = wasWrap;
            GUI.color = wasColor;
            Verse.Text.Font = wasFont;
            return y + h + GapXS;
        }

        // Retain the native font size when no explicit size is requested.
        public static GUIStyle SizedStyle(GameFont font, int textSize, TextAnchor anchor, bool wrap)
        {
            var wasFont = Verse.Text.Font;
            Verse.Text.Font = font;
            var style = new GUIStyle(Verse.Text.CurFontStyle)
            {
                alignment = anchor,
                clipping = TextClipping.Overflow,
                fontSize = textSize > 0 ? textSize : Verse.Text.CurFontStyle.fontSize,
                wordWrap = wrap,
            };
            Verse.Text.Font = wasFont;
            return style;
        }

        // Plain-text-only wrapped status/empty-state labels: catalog sprite keys remain
        // literal text. Measurement and drawing both use Verse's native wrapping; sprite
        // labels belong to RowLabel or a renderer with an explicit sprite-aware layout.
        public static float PlainStatusLabelHeight(string text, float width,
                                               GameFont font = GameFont.Small)
        {
            using (WidgetState.Save())
            {
                Verse.Text.Font = Real(font);
                Verse.Text.WordWrap = true;
                return Mathf.Max(LineHOf(font), Verse.Text.CalcHeight(
                    string.IsNullOrEmpty(text) ? " " : text, Mathf.Max(1f, width)));
            }
        }

        public static void PlainStatusLabel(Rect r, string text, Color color,
                                       GameFont font = GameFont.Small,
                                       TextAnchor anchor = TextAnchor.UpperLeft)
        {
            using (WidgetState.Save())
            {
                Verse.Text.Font = Real(font);
                Verse.Text.WordWrap = true;
                Verse.Text.Anchor = anchor;
                GUI.color = color;
                Widgets.Label(r, text ?? "");
            }
        }

        public static void RowLabel(Rect r, string text, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            if (TextSpriteCatalog.Shared.Contains(text))
            {
                DrawSpriteRowLabel(r, text, anchor, false);
                return;
            }

            DrawRowLabel(r, text, anchor,
                (line, label) => Widgets.Label(line, label));
        }

        // A preview tab uses italic text to signal that a single click may replace it. Keep this as
        // a row-label variant rather than changing Text.Font globally. The sidebar's action icon
        // and its project context are still rendered with their normal face.
        public static void RowLabel(Rect r, string text, TextAnchor anchor, bool italic)
        {
            if (!italic)
            {
                RowLabel(r, text, anchor);
                return;
            }

            if (TextSpriteCatalog.Shared.Contains(text))
            {
                DrawSpriteRowLabel(r, text, anchor, true);
                return;
            }

            DrawRowLabel(r, text, anchor, (line, label) =>
            {
                var source = Verse.Text.CurFontStyle;
                if (source == null)
                {
                    Widgets.Label(line, label);
                    return;
                }

                var style = new GUIStyle(source)
                {
                    fontStyle = Italic(source.fontStyle),
                    alignment = UpperAnchor(anchor),
                    wordWrap = false,
                };
                GUI.Label(line, label, style);
            });
        }

        static void DrawRowLabel(Rect r, string text, TextAnchor anchor,
                                 Action<Rect, string> draw)
        {
            using (WidgetState.Save())
            {
                // Truncate measures incorrectly while wrapping is enabled.
                Verse.Text.WordWrap = false;
                string label = TruncateText(text, r.width);
                Verse.Text.Anchor = UpperAnchor(anchor);

                draw(SnappedLine(r, anchor), label);
            }
        }

        static void DrawSpriteRowLabel(Rect r, string text, TextAnchor anchor, bool italic)
        {
            using (WidgetState.Save())
            {
                Verse.Text.WordWrap = false;
                Verse.Text.Anchor = UpperAnchor(anchor);
                float lineH = LineHOf(Verse.Text.Font);
                var line = SnappedLine(r, anchor);
                var source = Verse.Text.CurFontStyle;
                if (source == null)
                {
                    Widgets.Label(line, text ?? "");
                    return;
                }

                var style = new GUIStyle(source)
                {
                    fontStyle = italic ? Italic(source.fontStyle) : source.fontStyle,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = false,
                    clipping = TextClipping.Clip,
                };
                var layout = InlineTextLayout.Proportional(text, TextSpriteCatalog.Shared, lineH,
                    plain => style.CalcSize(new GUIContent(plain)).x, line.width);
                float offset = 0f;
                switch (anchor)
                {
                    case TextAnchor.UpperCenter:
                    case TextAnchor.MiddleCenter:
                    case TextAnchor.LowerCenter:
                        offset = Mathf.Max(0f, (line.width - layout.Width) * 0.5f);
                        break;
                    case TextAnchor.UpperRight:
                    case TextAnchor.MiddleRight:
                    case TextAnchor.LowerRight:
                        offset = Mathf.Max(0f, line.width - layout.Width);
                        break;
                }
                SharedTextRenderer.Draw(layout, line, lineH, style, offset);
            }
        }

        static Rect SnappedLine(Rect row, TextAnchor anchor)
        {
            // Keep UiFont's full line metric even when fractional scroll offsets snap inward.
            float lineH = LineHOf(Verse.Text.Font);
            float y = Slab.SnapY(row.y + (row.height - lineH) * VerticalFactor(anchor));
            float yMax = Slab.SnapY(y + lineH);
            return new Rect(row.x, y, row.width, Mathf.Max(lineH, yMax - y));
        }

        static FontStyle Italic(FontStyle style)
        {
            switch (style)
            {
                case FontStyle.Bold:
                case FontStyle.BoldAndItalic:
                    return FontStyle.BoldAndItalic;
                default:
                    return FontStyle.Italic;
            }
        }

        static TextAnchor UpperAnchor(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperCenter:
                case TextAnchor.MiddleCenter:
                case TextAnchor.LowerCenter:
                    return TextAnchor.UpperCenter;
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    return TextAnchor.UpperRight;
                default:
                    return TextAnchor.UpperLeft;
            }
        }

        static float VerticalFactor(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.LowerLeft:
                case TextAnchor.LowerCenter:
                case TextAnchor.LowerRight:
                    return 1f;
                case TextAnchor.MiddleLeft:
                case TextAnchor.MiddleCenter:
                case TextAnchor.MiddleRight:
                    return 0.5f;
                default:
                    return 0f;
            }
        }

        public static string Field(Rect r, string name, string text, bool on = true,
                                   string defaultValue = null)
        {
            bool released = on && TextEntryController.ReleaseFunctionKeyFocus(name);
            bool focused = on && !released && GUI.GetNameOfFocusedControl() == name;
            InputBackground(r, on, focused);

            var inner = r.ContractedBy(FieldPadX, FieldPadY);
            text = ResetDefault(r, ref inner, name, text, defaultValue, on);
            // Do not create a control that accepts input only to discard it next frame.
            if (!on) return Stated(inner, text, TextAnchor.MiddleLeft);
            if (released) return text;

            GUI.SetNextControlName(name);
            return TextEntryController.Draw(inner, text, false, focused, name);
        }

        // A native text field is used deliberately: its TextEditor supplies familiar drag,
        // Ctrl+C and right-click selection behavior. The value is restored after each draw,
        // so callers can expose a copyable value without making it editable state.
        public static void ReadOnlyField(Rect r, string name, string text)
        {
            bool released = TextEntryController.ReleaseFunctionKeyFocus(name);
            bool focused = !released && GUI.GetNameOfFocusedControl() == name;
            InputBackground(r, true, focused);

            if (released) return;
            var inner = r.ContractedBy(FieldPadX, FieldPadY);
            GUI.SetNextControlName(name);
            TextEntryController.Draw(inner, text, false, focused, name, true);
        }

        public static float AreaHeight(float width, string text, UiAreaResize resize, bool editable = true)
        {
            float contentWidth = Mathf.Max(1f, width - FieldPadX * 2f - UiTheme.ScrollbarW);
            float content = Text.CalcHeight(string.IsNullOrEmpty(text) ? " " : text, contentWidth)
                + FieldPadY * 4f + (editable ? UiAreaResize.CornerSize : 0f);
            return resize.MeasuredHeight(content);
        }

        public static string Area(Rect r, string name, string text, bool on = true,
                                  bool frame = true, string defaultValue = null,
                                  UiAreaResize resize = null)
        {
            // The caller lays out this pass using the measured height. Commit drag changes for the next
            // pass so the field, following rows and scroll extent share the same geometry.
            AreaSizingMenu(r, resize, on && GUI.enabled);
            resize?.Input(r, name, on && GUI.enabled);
            string value = AreaCore(r, name, text, on, frame, defaultValue, resize);
            resize?.Complete(r, on && GUI.enabled);
            return value;
        }

        static void AreaSizingMenu(Rect r, UiAreaResize resize, bool enabled)
        {
            if (resize == null || !enabled) return;
            var corner = new Rect(r.xMax - UiAreaResize.CornerSize,
                r.yMax - UiAreaResize.CornerSize, UiAreaResize.CornerSize, UiAreaResize.CornerSize);
            TooltipHandler.TipRegion(corner, resize.CanGrow
                ? "Drag to resize. Right-click for Fit to content." : "Drag to resize.");
            var e = Event.current;
            var type = UiEvent.RawType(e);
            if (!resize.CanGrow || !Mouse.IsOver(corner) ||
                !((type == EventType.MouseDown && e.button == 1) || type == EventType.ContextClick)) return;
            var lifetime = FieldLifetimeScope.Current;
            UiMenu.Open(new System.Collections.Generic.List<FloatMenuOption>
            {
                new FloatMenuOption("Fit to content", () =>
                {
                    if (lifetime.Alive) resize.FitToContent();
                }),
            });
            e.Use();
        }

        static string AreaCore(Rect r, string name, string text, bool on,
                               bool frame, string defaultValue, UiAreaResize resize)
        {
            bool released = on && TextEntryController.ReleaseFunctionKeyFocus(name);
            bool focused = on && !released && GUI.GetNameOfFocusedControl() == name;
            if (frame)
            {
                InputBackground(r, on, focused);
            }

            var inner = frame ? r.ContractedBy(FieldPadX, FieldPadY * 2f) : r;
            if (resize != null && on) inner.height = Mathf.Max(0f, inner.height - UiAreaResize.CornerSize);
            text = ResetDefault(r, ref inner, name, text, defaultValue, on, resize?.Scroll);
            if (released) return text;
            if (resize == null)
            {
                if (!on) return Stated(inner, text, TextAnchor.UpperLeft);
                GUI.SetNextControlName(name);
                return TextEntryController.Draw(inner, text, true, focused, name);
            }

            // Reserve scrollbar width consistently, even before content overflows.
            float width = Mathf.Max(1f, inner.width - UiTheme.ScrollbarW);
            var view = new Rect(0f, 0f, width,
                Mathf.Max(inner.height, Text.CalcHeight(string.IsNullOrEmpty(text) ? " " : text, width)));
            using (resize.Scroll.Scope(inner, view))
            {
                if (!on) return Stated(view, text, TextAnchor.UpperLeft);
                GUI.SetNextControlName(name);
                string value = TextEntryController.Draw(view, text, true, focused, name);
                var editor = TextEntryController.CurrentEditor(name);
                if (editor != null && (value != text || UiEvent.RawType(Event.current) == EventType.KeyDown))
                    resize.Scroll.Reveal(editor.graphicalCursorPos.y, UiTheme.LineH, inner.height);
                return value;
            }
        }

        // Null means no default. An empty string is a real default. Reserve a right-hand
        // gutter so wrapped text and selection never overlap the reset hit target.
        static string ResetDefault(Rect r, ref Rect inner, string name, string text,
                                   string defaultValue, bool on, SmoothScroll scroll = null)
        {
            if (defaultValue == null) return text;
            float size = Mathf.Min(CompactH, Mathf.Min(r.height, r.width));
            var button = new Rect(r.xMax - size, r.y, size, size);
            inner.width = Mathf.Max(0f, button.x - FieldPadX - inner.x);
            TooltipHandler.TipRegion(button, "Reset to default\n\n" +
                (defaultValue.Length == 0 ? "(empty)" : defaultValue));
            if (!UiLayout.IconButton(button, Icons.Refresh, Name,
                    size / 4f + IconInset / 2f,
                    on && (text ?? "") != defaultValue)) return text;

            // The reset caret starts at zero, including when the default still overflows
            // the area's viewport. Retire the outer scroll offset alongside native scrolling.
            scroll?.JumpTo(Vector2.zero);

            // Update Unity's focused editor as well as the form value. Otherwise its cached
            // text can restore the old value on the next draw.
            var editor = TextEntryController.CurrentEditor(name);
            if (editor != null)
            {
                editor.text = defaultValue;
                editor.cursorIndex = editor.selectIndex = 0;
                editor.scrollOffset = Vector2.zero;
            }
            GUI.changed = true;
            return defaultValue;
        }

        // The entry's frame on its own, for a caller drawing one box round more than one
        // thing - the command palette puts a prompt and an input inside a single entry.
        public static void FieldFrame(Rect r, bool focused)
        {
            InputBackground(r, true, focused);
        }

        // The text field with no frame of its own, for the same caller.
        public static string BareField(Rect r, string name, string text)
        {
            bool released = TextEntryController.ReleaseFunctionKeyFocus(name);
            if (released) return text;
            GUI.SetNextControlName(name);
            return TextEntryController.Draw(r, text, false,
                GUI.GetNameOfFocusedControl() == name, name);
        }

        static void InputBackground(Rect r, bool on, bool focused)
        {
            bool over = on && Mouse.IsOver(r);
            bool held = over && Input.GetMouseButton(0);

            // Draw the well first, then the same translucent hover/press wash used by
            // buttons and menu rows, and finally the edge. Keeping the edge last prevents
            // the wash from making a field one pixel heavier than its neighbours.
            Slab.Fill(r, on ? Well : Fade(Well, 0.5f));
            if (on && over) Slab.Fill(r, held ? BtnDown : BtnHover);
            Slab.Outline(r, focused ? Accent : BtnEdge);

            // The one control with real keyboard focus: the ring makes the active entry
            // legible even when its face is sitting inside a sidebar or popover.
            if (focused) Slab.Ring(r, FocusRing);
        }

        static string Stated(Rect r, string text, TextAnchor anchor)
        {
            using (WidgetState.Save())
            {
                Verse.Text.Anchor = anchor;
                GUI.color = Faint;
                Widgets.Label(r, text ?? "");
            }
            return text;
        }
    }
}
