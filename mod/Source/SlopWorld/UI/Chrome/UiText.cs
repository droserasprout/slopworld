using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public abstract class UiText : UiTheme
    {
        // Wrapped status and empty-state text has the same font/anchor/color contract across
        // pages. Keep its measurement beside the draw path so dynamic fonts do not make a
        // caller reserve a height from a different face.
        public static float StatusLabelHeight(string text, float width,
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

        public static void StatusLabel(Rect r, string text, Color color,
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
            if (UiEmoji.HasSupported(text))
            {
                DrawEmojiRowLabel(r, text, anchor, false);
                return;
            }

            DrawRowLabel(r, text, anchor,
                (line, label) => Widgets.Label(line, label));
        }

        // A preview tab uses italic text to signal that a single click may replace it. Keep
        // this as a row-label variant rather than changing Text.Font globally: the sidebar's
        // action icon and its project context are still rendered with their normal face.
        public static void RowLabel(Rect r, string text, TextAnchor anchor, bool italic)
        {
            if (!italic)
            {
                RowLabel(r, text, anchor);
                return;
            }

            if (UiEmoji.HasSupported(text))
            {
                DrawEmojiRowLabel(r, text, anchor, true);
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

                // Text.LineHeightOf is the box UiFont sized to hold the face. Drawing
                // into a fresh CalcHeight box was shorter for some dynamic sizes, cutting
                // descenders despite the row itself having enough space for them.
                float lineH = LineHOf(Verse.Text.Font);
                float y = Slab.SnapY(r.y + (r.height - lineH) * VerticalFactor(anchor));
                float yMax = Slab.SnapY(y + lineH);
                // A fractional scroll offset can snap the two edges inward by one pixel;
                // never let screen-pixel snapping make the label shorter than its metric.
                float h = Mathf.Max(lineH, yMax - y);
                draw(new Rect(r.x, y, r.width, h), label);
            }
        }

        static void DrawEmojiRowLabel(Rect r, string text, TextAnchor anchor, bool italic)
        {
            using (WidgetState.Save())
            {
                Verse.Text.WordWrap = false;
                Verse.Text.Anchor = UpperAnchor(anchor);
                float lineH = LineHOf(Verse.Text.Font);
                float y = Slab.SnapY(r.y + (r.height - lineH) * VerticalFactor(anchor));
                float yMax = Slab.SnapY(y + lineH);
                float h = Mathf.Max(lineH, yMax - y);
                var line = new Rect(r.x, y, r.width, h);
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
                string label = text ?? "";
                float width = UiEmoji.Measure(label, lineH,
                    plain => Verse.Text.CalcSize(plain).x);
                float x = line.x;
                switch (anchor)
                {
                    case TextAnchor.UpperCenter:
                    case TextAnchor.MiddleCenter:
                    case TextAnchor.LowerCenter:
                        x += Mathf.Max(0f, (line.width - width) * 0.5f);
                        break;
                    case TextAnchor.UpperRight:
                    case TextAnchor.MiddleRight:
                    case TextAnchor.LowerRight:
                        x += Mathf.Max(0f, line.width - width);
                        break;
                }

                int from = 0;
                for (int i = 0; i < label.Length; i++)
                {
                    int length;
                    if (!TerminalEmoji.IsSupportedAt(label, i, out length))
                        continue;

                    DrawInlineText(label, from, i, ref x, y, lineH, style);
                    Color textColor = GUI.color;
                    GUI.color = Color.white;
                    // The atlas is a color texture; never tint it with a button's text color.
                    int drawnLength;
                    bool drawn = TerminalEmoji.TryDrawInline(label, i, x, y, lineH,
                        out drawnLength);
                    GUI.color = textColor;
                    if (drawn)
                    {
                        x += lineH;
                    }
                    else
                    {
                        // Keep the fallback path safe if a packaged atlas is missing.
                        GUI.Label(new Rect(x, y, lineH * 2f, lineH),
                            label.Substring(i, length), style);
                        x += lineH * 2f;
                    }
                    i += length - 1;
                    from = i + 1;
                }
                DrawInlineText(label, from, label.Length, ref x, y, lineH, style);
            }
        }

        static void DrawInlineText(string text, int from, int to, ref float x,
                                   float y, float h, GUIStyle style)
        {
            if (to <= from) return;
            string segment = text.Substring(from, to - from);
            float width = Verse.Text.CalcSize(segment).x;
            GUI.Label(new Rect(x, y, width + 1f, h), segment, style);
            x += width;
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

        public static string Area(Rect r, string name, string text, bool on = true,
                                  bool frame = true, string defaultValue = null)
        {
            bool released = on && TextEntryController.ReleaseFunctionKeyFocus(name);
            bool focused = on && !released && GUI.GetNameOfFocusedControl() == name;
            if (frame)
            {
                InputBackground(r, on, focused);
            }

            var inner = frame ? r.ContractedBy(FieldPadX, FieldPadY * 2f) : r;
            text = ResetDefault(r, ref inner, name, text, defaultValue, on);
            if (!on) return Stated(inner, text, TextAnchor.UpperLeft);
            if (released) return text;

            GUI.SetNextControlName(name);
            return TextEntryController.Draw(inner, text, true, focused, name);
        }

        // Null means no default; an empty string is a real default. Reserve a right-hand
        // gutter so wrapped text and selection never overlap the reset hit target.
        static string ResetDefault(Rect r, ref Rect inner, string name, string text,
                                   string defaultValue, bool on)
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

            // Update Unity's focused editor as well as the form value; otherwise its cached
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
