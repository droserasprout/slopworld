using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    // Shared scheme-driven opaque chrome; Slab owns fills/edges and fixed gaps keep controls
    // on the screen pixel grid.
    public abstract class SlopTheme
    {
        // ---- Surfaces and semantic colors. These are named for SlopWorld's jobs rather
        // than for a borrowed toolkit's widgets, and they are the only names anything else
        // in the mod knows: the values behind them belong to the scheme the player picked,
        // and are read through here so a scheme lands everywhere at once. See UIScheme.

        public static Color Accent => UIScheme.Current.Accent;
        public static Color Destructive => UIScheme.Current.Destructive;

        public static Color WindowBg => UIScheme.Current.WindowBg;
        public static Color ViewBg => UIScheme.Current.ViewBg;
        public static Color PopoverBg => UIScheme.Current.PopoverBg;

        public static Color Lead => UIScheme.Current.Lead;
        public static Color Name => UIScheme.Current.Name;
        public static Color Dim => UIScheme.Current.Dim;
        public static Color Faint => UIScheme.Current.Faint;
        public static Color Off => UIScheme.Current.Off;

        public static Color Bad => UIScheme.Current.Bad;
        public static Color Warn => UIScheme.Current.Warn;
        public static Color Global => UIScheme.Current.Global;

        // Panel is the one surface allowed to show a trace of the map beneath it. All
        // controls and popovers use the opaque surfaces above.
        public static Color Panel => UIScheme.Current.Panel;
        public static Color OfflineBg => UIScheme.Current.OfflineBg;

        // A wash under text that stands on the map, where there is no surface to put it on.
        public static Color Scrim => UIScheme.Current.Scrim;

        // Structural lines are a restrained wash of the scheme's own light. EdgeLit is
        // reserved for the resize grip and other places where the pointer is actively on
        // the structure.
        public static Color Edge => UIScheme.Current.Edge;
        public static Color EdgeLit => UIScheme.Current.EdgeLit;
        public static Color ScrollTrough => UIScheme.Current.ScrollTrough;
        public static Color ScrollThumb => UIScheme.Current.ScrollThumb;
        public static Color ScrollThumbHover => UIScheme.Current.ScrollThumbHover;
        public static Color ScrollThumbHeld => UIScheme.Current.ScrollThumbHeld;

        public static Color Well => UIScheme.Current.Well;

        // One green for "this is up" and "this is on"; Yes is the name the forms ask for it
        // by, and the status marker is the same color saying the same thing.
        public static Color Yes => UIScheme.Current.Yes;

        public static Color RowBg => UIScheme.Current.RowBg;
        public static Color RowOn => UIScheme.Current.RowOn;
        public static Color Sel => UIScheme.Current.Sel;
        public static Color Hover => UIScheme.Current.Hover;

        public static Color StateWorking => UIScheme.Current.StateWorking;
        public static Color StateWaiting => UIScheme.Current.StateWaiting;
        public static Color StateIdle => UIScheme.Current.StateIdle;
        public static Color StateDown => UIScheme.Current.StateDown;
        public static Color Info => UIScheme.Current.StateWorking;

        public static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

        protected static Color Lighten(Color c, float by)
        {
            var to = by >= 0f ? Color.white : Color.black;
            float t = Mathf.Abs(by);
            return new Color(Mathf.Lerp(c.r, to.r, t), Mathf.Lerp(c.g, to.g, t),
                Mathf.Lerp(c.b, to.b, t), c.a);
        }

        // Opacity is used only for disabled or overlaid states; base surfaces stay opaque.
        public static Color Fade(Color c, float by) =>
            new Color(c.r, c.g, c.b, c.a * by);

        // Accent and destructive buttons use a lightness step; ordinary buttons use the
        // same translucent white faces over every shared surface.
        protected static Color Step(Color c, bool over, bool held) =>
            held ? Lighten(c, -0.15f) : over ? Lighten(c, 0.10f) : c;

        public static float LineH => LineHOf(GameFont.Small);

        public static float FieldH => CompactH;
        public static float RowH => LineH + GapXS + 2f;

        public static float HeaderH => LineHOf(GameFont.Medium) + GapS;

        public static float LineHOf(GameFont font) =>
            Mathf.Ceil(Verse.Text.LineHeightOf(Real(font)));

        // Verse silently promotes Tiny when the current language or display cannot support it.
        public static GameFont Real(GameFont font) =>
            font == GameFont.Tiny && !Verse.Text.TinyFontSupported ? GameFont.Small : font;

        public static float TinyH => LineHOf(GameFont.Tiny);
        public static float TinyRowH => TinyH + 2f;

        public static float Wide(string text)
        {
            bool wrap = Verse.Text.WordWrap;
            Verse.Text.WordWrap = false;
            float w = Verse.Text.CalcSize(text ?? "").x;
            Verse.Text.WordWrap = wrap;
            return w;
        }

        public enum Btn
        {
            Default,   // the ordinary press: Reload, Browse, Edit.
            Primary,   // what the window was opened to do. One per bar, or it means nothing.
            Danger,    // takes something away. Still asks first; this is so it is read first.
            Ghost,     // there, but not competing - a press beside a press that matters more.
        }

        protected static Color BtnEdge => Edge;

        protected static Color BtnFace => UIScheme.Current.BtnFace;
        protected static Color BtnHover => UIScheme.Current.BtnHover;
        protected static Color BtnDown => UIScheme.Current.BtnDown;

        // A ghost button has no face at rest; its rectangular hit area appears on hover.
        protected static Color GhostFace => Clear;

        protected static Color FocusRing => Accent;

        protected static Color KnobFace => UIScheme.Current.Knob;
        protected static Color CheckFace => UIScheme.Current.CheckFace;

        protected static Color PrimeFace => Accent;
        protected static Color DangerFace => Destructive;

        public const float BtnH = 30f;
        public const float ButtonPadX = 12f;
        public const float ButtonMinW = 76f;
        public const float FieldPadX = 6f;
        public const float FieldPadY = 2f;
        public const float IconInset = 2f;
        public const float IconW = 18f;
        public const float ScrollbarW = 18f;
        public const float ScrollTrackW = 10f;
        public const float ScrollThumbInset = 2f;
        public const float MenuPadX = 12f;
        public const float MenuPadY = 0f;
        public const float StatusMarker = 8f;

        // Single-line controls share one compact hit target. Menus, fields and small row
        // buttons used to differ by a pixel, which was enough to make a form and the menu
        // opened from it feel like two widget kits.
        public static float CompactH => Mathf.Max(LineH + GapXS, 22f);
        public static float RowBtnH => CompactH;

        // A dropdown's rows carry one line each and are read as a block, so they sit as close
        // as the line will let them - a gap step tighter than the palette's, which is a list
        // scrolled and stepped through with the keyboard and wants the hit target.
        public static float MenuRowH => CompactH;
        public static float PaletteRowH => Mathf.Max(LineH + GapS, 26f);

        public const float GapXS = 4f;   // a label and the box it names
        public const float GapS = 8f;    // one control and the next
        public const float GapM = 16f;   // one group of controls and the next
        public const float GapL = 24f;   // one section and the next

    }

    public abstract class SlopButtons : SlopText
    {
        public static bool Button(Rect r, string label, Btn kind = Btn.Default, bool on = true)
        {
            bool over = on && Mouse.IsOver(r);
            bool held = over && Input.GetMouseButton(0);

            // A button changes face, never shape. Pressing a rectangular instrument should not
            // make the layout jump underneath the pointer.
            ButtonBackground(r, kind, on, over, held);

            Color face, text;
            switch (kind)
            {
                case Btn.Primary:
                    face = Step(PrimeFace, over, held);
                    text = over ? UIScheme.TextOn(face) : UIScheme.Current.AccentText;
                    break;
                case Btn.Danger:
                    face = Step(DangerFace, over, held);
                    text = over ? UIScheme.TextOn(face) : UIScheme.Current.DestructiveText;
                    break;
                case Btn.Ghost:
                    face = held ? BtnDown : over ? BtnHover : GhostFace;
                    text = over ? Lead : Name;
                    break;
                default:
                    face = held ? BtnDown : over ? BtnHover : Well;
                    text = Lead; break;
            }

            if (!on) text = Fade(text, 0.5f);

            var wasColor = GUI.color;
            GUI.color = text;
            RowLabel(r, label, TextAnchor.MiddleCenter);
            GUI.color = wasColor;

            if (!on || !Widgets.ButtonInvisible(r)) return false;

            SoundDefOf.Click.PlayOneShotOnCamera();
            return true;
        }

        public static void ButtonBackground(Rect r, Btn kind, bool on, bool over, bool held)
        {
            ButtonBackground(r, kind, on, over, held, Well);
        }

        // Session gizmos sit over the map beside the sidebar. Give their resting face the
        // same opaque panel surface so the action strip reads as part of the chrome too.
        public static void ActionButtonBackground(Rect r, Btn kind, bool on, bool over,
                                                  bool held)
        {
            ButtonBackground(r, kind, on, over, held, Panel);
        }

        static void ButtonBackground(Rect r, Btn kind, bool on, bool over, bool held,
                                     Color defaultFace)
        {
            Color face;
            switch (kind)
            {
                case Btn.Primary:
                    face = Step(PrimeFace, over, held);
                    break;
                case Btn.Danger:
                    face = Step(DangerFace, over, held);
                    break;
                case Btn.Ghost:
                    face = held ? BtnDown : over ? BtnHover : GhostFace;
                    break;
                default:
                    face = held ? BtnDown : over ? BtnHover : defaultFace;
                    break;
            }

            if (!on) face = Fade(face, 0.5f);

            bool solid = kind == Btn.Primary || kind == Btn.Danger;
            var edge = solid || (kind == Btn.Ghost && !over && !held)
                ? Clear
                : on ? BtnEdge : Fade(BtnEdge, 0.5f);

            Slab.Box(r, face, edge);
        }

    }

    public abstract class SlopText : SlopTheme
    {
        public static void RowLabel(Rect r, string text, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            // Truncate measures incorrectly while wrapping is enabled.
            bool wrap = Verse.Text.WordWrap;
            var wasAnchor = Verse.Text.Anchor;
            try
            {
                Verse.Text.WordWrap = false;
                string label = (text ?? "").Truncate(Mathf.Max(1f, r.width));

                Verse.Text.Anchor = UpperAnchor(anchor);
                // Text.LineHeightOf is the box SlopUIFont sized to hold the face. Drawing
                // into a fresh CalcHeight box was shorter for some dynamic sizes, cutting
                // descenders despite the row itself having enough space for them.
                float lineH = LineHOf(Verse.Text.Font);
                float y = Slab.SnapY(r.y + (r.height - lineH) * VerticalFactor(anchor));
                float yMax = Slab.SnapY(y + lineH);
                // A fractional scroll offset can snap the two edges inward by one pixel;
                // never let screen-pixel snapping make the label shorter than its metric.
                float h = Mathf.Max(lineH, yMax - y);
                Widgets.Label(new Rect(r.x, y, r.width, h), label);
            }
            finally
            {
                Verse.Text.Anchor = wasAnchor;
                Verse.Text.WordWrap = wrap;
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

        public static string Field(Rect r, string name, string text, bool on = true)
        {
            bool focused = on && ReleaseFunctionKeyFocus(name);
            InputBackground(r, on, focused);

            var inner = r.ContractedBy(FieldPadX, FieldPadY);
            // Do not create a control that accepts input only to discard it next frame.
            if (!on) return Stated(inner, text, TextAnchor.MiddleLeft);

            GUI.SetNextControlName(name);
            return TextEntry(inner, text, false, focused, name);
        }

        public static string Area(Rect r, string name, string text, bool on = true,
                                  bool frame = true)
        {
            bool focused = on && ReleaseFunctionKeyFocus(name);
            if (frame)
            {
                InputBackground(r, on, focused);
            }

            var inner = frame ? r.ContractedBy(FieldPadX, FieldPadY * 2f) : r;
            if (!on) return Stated(inner, text, TextAnchor.UpperLeft);

            GUI.SetNextControlName(name);
            return TextEntry(inner, text, true, focused, name);
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
            GUI.SetNextControlName(name);
            return TextEntry(r, text, false, ReleaseFunctionKeyFocus(name), name);
        }

        // Unity's text controls consume function keys while focused, before the game or the
        // terminal can dispatch them. Release only this field's focus so bare F-keys reach the
        // mod's chrome and shifted F-keys remain available to the terminal. Ordinary typing and
        // function keys in unrelated controls are unaffected.
        static bool ReleaseFunctionKeyFocus(string name)
        {
            if (GUI.GetNameOfFocusedControl() != name) return false;

            var e = Event.current;
            if (e == null || e.rawType != EventType.KeyDown
                || e.keyCode < KeyCode.F1 || e.keyCode > KeyCode.F15)
                return true;

            GUI.FocusControl(null);
            return false;
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

        sealed class PendingFieldEdit
        {
            public readonly string Name;
            public readonly Action<TextEditor> Apply;

            public PendingFieldEdit(string name, Action<TextEditor> apply)
            {
                Name = name;
                Apply = apply;
            }
        }

        static readonly Queue<PendingFieldEdit> PendingEdits =
            new Queue<PendingFieldEdit>();

        static string TextEntry(Rect r, string text, bool area, bool focused, string name)
        {
            var wasColor = GUI.color;
            bool over = Mouse.IsOver(r);
            // Most callers leave GUI.color white. Give ordinary entries the same quiet text
            // ramp as menu and sidebar labels, while preserving deliberate placeholder/error
            // tints supplied by a caller.
            if (wasColor == Color.white)
                GUI.color = focused || over ? Lead : Name;

            var e = Event.current;
            bool eventOver = e != null && r.Contains(e.mousePosition);
            int button = -1;
            bool mouseDown = eventOver && MouseDown(e, out button);
            bool replay = mouseDown && (e.type == EventType.Used || e.type == EventType.ContextClick);
            var oldType = replay ? e.type : EventType.Ignore;
            var beforeEditor = mouseDown && button == 1 ? CurrentEditor(name) : null;
            bool keepSelection = beforeEditor != null && beforeEditor.IsOverSelection(e.mousePosition);
            int beforeCursor = keepSelection ? beforeEditor.cursorIndex : 0;
            int beforeSelect = keepSelection ? beforeEditor.selectIndex : 0;

            // WindowStack may have consumed the event before this window's contents run. Give
            // the native editor its mouse-down once, then leave the event Used as before. This
            // is what lets a click focus a field even when it sits below an absorbing window.
            if (replay) e.type = EventType.MouseDown;

            try
            {
                var style = Bare(area ? Verse.Text.CurTextAreaStyle
                                      : Verse.Text.CurTextFieldStyle, area);
                var result = area
                    ? GUI.TextArea(r, text ?? "", style)
                    : GUI.TextField(r, text ?? "", style);

                var editor = CurrentEditor(name);
                if (keepSelection && editor != null)
                {
                    editor.cursorIndex = beforeCursor;
                    editor.selectIndex = beforeSelect;
                }
                ApplyPendingEdits(name, editor);
                if (editor != null) result = editor.text;

                if (mouseDown && button == 2)
                {
                    RequestPaste(name, area, primary: true);
                    e.Use();
                }
                else if (mouseDown && button == 1)
                {
                    OpenContextMenu(name, area, editor);
                    e.Use();
                }

                return result;
            }
            finally
            {
                // A ContextClick that the native field did not consume still belongs to us.
                // Restore only an event that is genuinely still live; a native Use() must stay
                // Used so lower layers do not interpret the same click a second time.
                if (replay && e.type != EventType.Used) e.type = oldType;
                GUI.color = wasColor;
            }
        }

        static bool MouseDown(Event e, out int button)
        {
            button = e == null ? -1 : e.button;
            if (e == null) return false;

            var type = e.type == EventType.Used ? e.rawType : e.type;
            if (type == EventType.ContextClick)
            {
                button = 1;
                return true;
            }
            return type == EventType.MouseDown;
        }

        static TextEditor CurrentEditor(string name)
        {
            if (GUI.GetNameOfFocusedControl() != name) return null;
            int id = GUIUtility.keyboardControl;
            return id == 0
                ? null
                : GUIUtility.QueryStateObject(typeof(TextEditor), id) as TextEditor;
        }

        static void ApplyPendingEdits(string name, TextEditor editor)
        {
            if (editor == null || PendingEdits.Count == 0) return;

            if (GUI.GetNameOfFocusedControl() != name) return;
            var keep = new Queue<PendingFieldEdit>();
            while (PendingEdits.Count > 0)
            {
                var edit = PendingEdits.Dequeue();
                if (edit.Name != name)
                {
                    keep.Enqueue(edit);
                    continue;
                }

                edit.Apply(editor);
                GUI.changed = true;
            }

            while (keep.Count > 0) PendingEdits.Enqueue(keep.Dequeue());
        }

        static void QueueEdit(string name, Action<TextEditor> apply)
        {
            if (string.IsNullOrEmpty(name) || apply == null) return;
            PendingEdits.Enqueue(new PendingFieldEdit(name, apply));
        }

        static string SingleLinePaste(string text, bool area)
        {
            if (area || string.IsNullOrEmpty(text)) return text ?? "";
            return text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        }

        static void QueuePaste(string name, string text, bool area)
        {
            if (string.IsNullOrEmpty(text)) return;
            string insert = SingleLinePaste(text, area);
            QueueEdit(name, editor => editor.ReplaceSelection(insert));
        }

        static void RequestPaste(string name, bool area, bool primary)
        {
            if (string.IsNullOrEmpty(name)) return;

            if (!SessionHub.Instance.Capabilities.Clipboard)
            {
                // The daemon is unavailable in sidecar mode; Unity's local buffer is the only
                // clipboard surface the game can access there.
                QueuePaste(name, GUIUtility.systemCopyBuffer, area);
                return;
            }

            string path = primary ? "/api/clipboard/primary/text" : "/api/clipboard/text";
            SlopClient.Get(path,
                j => QueuePaste(name, j["text"].AsString(), area),
                _ =>
                {
                    if (!primary) QueuePaste(name, GUIUtility.systemCopyBuffer, area);
                });
        }

        static void OpenContextMenu(string name, bool area, TextEditor editor)
        {
            if (string.IsNullOrEmpty(name)) return;

            string selected = editor?.SelectedText ?? "";
            var options = new List<FloatMenuOption>();

            var cut = new FloatMenuOption("Cut", () =>
            {
                SlopClipboard.Copy(selected);
                QueueEdit(name, e => e.DeleteSelection());
            });
            cut.Disabled = selected.Length == 0;
            options.Add(cut);

            var copy = new FloatMenuOption("Copy", () => SlopClipboard.Copy(selected));
            copy.Disabled = selected.Length == 0;
            options.Add(copy);

            options.Add(new FloatMenuOption("Paste", () => RequestPaste(name, area, false)));
            options.Add(new FloatMenuOption("Select all", () =>
                QueueEdit(name, e => e.SelectAll())));

            SlopMenu.Open(options);
        }

        static GUIStyle Bare(GUIStyle of, bool area)
        {
            var style = new GUIStyle(of);

            // Vanilla's skin can leave an active/on-state texture behind even after the
            // ordinary states are cleared. Strip every state so Slab is the only owner of
            // field chrome, just as it is for buttons, checkboxes and context menus.
            style.normal.background = null;
            style.hover.background = null;
            style.active.background = null;
            style.focused.background = null;
            style.onNormal.background = null;
            style.onHover.background = null;
            style.onActive.background = null;
            style.onFocused.background = null;

            // The outer rect already supplies the rhythm. Vanilla text-entry padding and
            // offsets otherwise make the same font sit differently in fields and menu rows.
            style.border = new RectOffset();
            style.margin = new RectOffset();
            style.overflow = new RectOffset();
            style.padding = new RectOffset();
            style.contentOffset = Vector2.zero;
            style.alignment = area ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            style.wordWrap = area;
            // Dynamic faces can put a descender below the height reported by IMGUI. Keep
            // input text from losing its bottom pixel while the outer Slab still owns the
            // field's exact geometry.
            style.clipping = TextClipping.Overflow;

            // Let GUI.color carry the scheme ramp (and caller placeholder tints); a skin
            // with a dark-entry text color must not turn the same control black on a dark well.
            style.normal.textColor = Color.white;
            style.hover.textColor = Color.white;
            style.active.textColor = Color.white;
            style.focused.textColor = Color.white;
            style.onNormal.textColor = Color.white;
            style.onHover.textColor = Color.white;
            style.onActive.textColor = Color.white;
            style.onFocused.textColor = Color.white;
            return style;
        }

        static string Stated(Rect r, string text, TextAnchor anchor)
        {
            var wasAnchor = Verse.Text.Anchor;
            var wasColor = GUI.color;
            Verse.Text.Anchor = anchor;
            GUI.color = Faint;
            try
            {
                Widgets.Label(r, text ?? "");
            }
            finally
            {
                GUI.color = wasColor;
                Verse.Text.Anchor = wasAnchor;
            }
            return text;
        }

        // Draw the checkbox indicator only; the row owns hit testing. Return its reserved width
        // so callers can place the adjacent label.
        public static float TickW => Mathf.Round(LineH * 0.8f);
        public static float TickColW => TickW + GapS;

        public static Rect TickBox(Rect r, bool on, bool locked = false)
        {
            float size = Mathf.Min(TickW, r.height - 2f);
            var box = new Rect(r.x, r.y + (r.height - size) / 2f, size, size);

            var face = on ? CheckFace : Well;
            var edge = on ? Clear : BtnEdge;
            if (locked) { face = Fade(face, 0.5f); edge = Fade(edge, 0.5f); }
            Slab.Box(box, face, edge);
            if (on)
            {
                GUI.color = locked ? Faint : UIScheme.TextOn(face);
                GUI.DrawTexture(box.ContractedBy(IconInset), Icons.Check);
                GUI.color = Color.white;
            }
            return box;
        }

        public static bool Checkbox(Rect r, string label, bool on, string tip = null,
                                    bool locked = false, bool warn = false)
        {
            bool over = !locked && Mouse.IsOver(r);
            if (over) Slab.Fill(r, Hover);
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);

            var box = TickBox(new Rect(r.x + 1f, r.y, TickW, r.height), on, locked);

            GUI.color = locked ? Faint : warn ? Warn : over ? Lead : Name;
            RowLabel(new Rect(box.xMax + GapS, r.y, r.xMax - box.xMax - GapS, r.height), label);
            GUI.color = Color.white;

            if (locked || !Widgets.ButtonInvisible(r)) return on;

            SoundDefOf.Click.PlayOneShotOnCamera();
            return !on;
        }

        public static string Field(Listing_Standard l, string name, string text, bool on = true) =>
            Field(l.GetRect(FieldH), name, text, on);

        public static bool Checkbox(Listing_Standard l, string label, bool on, string tip = null) =>
            Checkbox(l.GetRect(RowH), label, on, tip);

        // A dropdown caret. Collapse is vanilla's downward triangle. Keep the draw in the
        // active GUI group; rotating through GUI.matrix makes a caret drift when that group
        // has a scroll translation.
        static void Chevron(Rect r, Color c, bool open)
        {
            var wasColor = GUI.color;
            try
            {
                GUI.color = c;
                // A negative UV height mirrors the down triangle for the open/up state while
                // leaving the destination rect in the current group's coordinate space.
                GUI.DrawTextureWithTexCoords(r, TexButton.Collapse,
                    open ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f));
            }
            finally
            {
                GUI.color = wasColor;
            }
        }

        // A labelled combobox: caption on one line, then a framed value box with the current
        // value and a down-chevron. Unlike Button it reads as a chooser - caption and value
        // stay apart, and the chevron says the value opens a list rather than firing an action.
        // `box` comes back in the current GUI group so the caller can anchor its menu with
        // `MenuAt(box)`.
        public static bool Select(Rect r, string caption, string value, out Rect box,
                                  string tip = null, bool on = true, bool open = false,
                                  float forcedWidth = 0f)
        {
            float labelH = LineH;
            var label = new Rect(r.x, r.y, r.width, labelH);
            float chevron = Mathf.Round(LineH * 0.55f);
            float boxW = forcedWidth > 0f
                ? Mathf.Min(r.width, forcedWidth)
                : Mathf.Min(r.width,
                    Mathf.Max(Wide(value) + ButtonPadX * 2f, ButtonMinW)
                        + chevron + GapS);
            box = new Rect(r.x, r.y + labelH + GapXS, boxW, CompactH);

            GUI.color = on ? Name : Fade(Name, 0.5f);
            RowLabel(label, caption);

            bool over = on && Mouse.IsOver(box);
            bool held = over && Input.GetMouseButton(0);
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);

            var face = !on ? Fade(Well, 0.5f) : held ? BtnDown : over ? BtnHover : Well;
            Slab.Box(box, face, on ? BtnEdge : Fade(BtnEdge, 0.5f));
            if (open) Slab.Ring(box, Accent);

            // A hairline splits the value from the chevron, mirroring the submenu marker SlopMenu
            // draws on a nested row.
            float caretX = box.xMax - ButtonPadX - chevron;
            Slab.VHairline(new Rect(caretX - GapS, box.y + GapXS, 1f, box.height - GapXS * 2f),
                           on ? BtnEdge : Fade(BtnEdge, 0.5f));
            Chevron(new Rect(caretX, box.y + (box.height - chevron) / 2f, chevron, chevron),
                    !on ? Fade(Faint, 0.5f) : over ? Accent : Faint, open);

            GUI.color = !on ? Fade(Lead, 0.5f) : over ? Lead : Name;
            float textX = box.x + ButtonPadX;
            RowLabel(new Rect(textX, box.y, Mathf.Max(0f, caretX - GapS - textX), box.height),
                     value);
            GUI.color = Color.white;

            if (!on || !Widgets.ButtonInvisible(box)) return false;
            SoundDefOf.Click.PlayOneShotOnCamera();
            return true;
        }

        public static bool Select(Rect r, string caption, string value,
                                  IEnumerable<string> choices, out Rect box,
                                  string tip = null, bool on = true, bool open = false)
        {
            var labels = new List<string> { value };
            if (choices != null) labels.AddRange(choices);

            float width = SlopMenu.WidthFor(labels);
            return Select(r, caption, value, out box, tip, on, open, width);
        }

        public static bool Select(Listing_Standard l, string caption, string value,
                                  out Rect box, string tip = null, bool on = true) =>
            Select(l.GetRect(LineH + GapXS + CompactH), caption, value, out box, tip, on);

        public static bool Select(Listing_Standard l, string caption, string value,
                                  IEnumerable<string> choices, out Rect box,
                                  string tip = null, bool on = true) =>
            Select(l.GetRect(LineH + GapXS + CompactH), caption, value, choices,
                out box, tip, on);

        // `r` is local to the active GUI group; SlopMenu's root position is in UI screen
        // coordinates. Convert while the group is still active, before the caller adds the
        // menu to the window stack.
        public static Vector2 MenuAt(Rect r) =>
            UI.GUIToScreenPoint(new Vector2(r.x, r.yMax));

        // A slider in the same flat chrome as the fields and checkboxes. The label is part
        // of the control rather than a separate Listing_Standard row, so a page of several
        // levels reads as one compact mixer. Mouse capture belongs to IMGUI's hot control:
        // dragging may leave the track without losing the knob.
        public static float Slider(Listing_Standard l, string label, float value,
                                   string tip = null) =>
            Slider(l, label, value, 0f, 1f,
                Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%", tip);

        // The same control over a range that is not nought to one, with the readout written
        // by the caller: a font size and a dimming fraction are not percentages of anything,
        // and the three pages that wanted them were the three still on vanilla's slider.
        public static float Slider(Listing_Standard l, string label, float value,
                                   float min, float max, string readout, string tip = null) =>
            Slider(l, label, value, min, max, readout, out _, tip);

        // Apply geometry-changing values after release: live UI scaling moves the track under the
        // pointer. `held` reports whether the hot control still owns the knob.
        static int _sliderGrabId;
        static float _sliderGrab;

        public static float Slider(Listing_Standard l, string label, float value,
                                   float min, float max, string readout, out bool held,
                                   string tip = null)
        {
            var r = l.GetRect(RowH + GapS);
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);

            float span = max - min;
            float at = span <= 0f ? 0f : Mathf.Clamp01((value - min) / span);
            return min + Track(r, label, at, readout, out held) * span;
        }

        static float Track(Rect r, string label, float value, string readout, out bool held)
        {
            const float valueW = 46f;
            const float knobW = 12f;
            float labelW = Mathf.Min(Mathf.Max(Wide(label) + GapM, 120f), r.width * 0.42f);
            var labelRect = new Rect(r.x, r.y, labelW, RowH);
            var valueRect = new Rect(r.xMax - valueW, r.y, valueW, RowH);
            var track = new Rect(labelRect.xMax + GapS, r.y + (RowH - 8f) / 2f,
                Mathf.Max(1f, valueRect.x - GapS - labelRect.xMax - GapS), 8f);

            GUI.color = Name;
            RowLabel(labelRect, label);
            RowLabel(valueRect, readout, TextAnchor.MiddleRight);
            GUI.color = Color.white;

            int id = GUIUtility.GetControlID(FocusType.Passive, track);
            var e = Event.current;
            var hit = new Rect(track.x - knobW / 2f, r.y, track.width + knobW, RowH);
            if (e.type == EventType.MouseDown && e.button == 0 && hit.Contains(e.mousePosition))
            {
                // Keep the point where the knob was picked up under the pointer. Without
                // this, grabbing either side of the square makes the first drag recenter it.
                GUIUtility.hotControl = id;
                _sliderGrabId = id;
                _sliderGrab = e.mousePosition.x - Mathf.Lerp(track.x, track.xMax, value);
                e.Use();
            }
            if (GUIUtility.hotControl == id)
            {
                if (e.type == EventType.MouseDrag || e.type == EventType.MouseDown)
                {
                    float grab = _sliderGrabId == id ? _sliderGrab : 0f;
                    value = Mathf.Clamp01(Mathf.InverseLerp(track.x, track.xMax,
                        e.mousePosition.x - grab));
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && e.button == 0)
                {
                    float grab = _sliderGrabId == id ? _sliderGrab : 0f;
                    value = Mathf.Clamp01(Mathf.InverseLerp(track.x, track.xMax,
                        e.mousePosition.x - grab));
                    GUIUtility.hotControl = 0;
                    if (_sliderGrabId == id) _sliderGrabId = 0;
                    e.Use();
                }
            }

            Slab.Box(track, Well, BtnEdge);
            var fill = new Rect(track.x, track.y, track.width * Mathf.Clamp01(value), track.height);
            if (fill.width > 0f) Slab.Fill(fill, PrimeFace);

            // The slider's square light knob makes a row of levels readable at a glance.
            float knobX = Mathf.Lerp(track.x, track.xMax, Mathf.Clamp01(value));
            bool grabbed = held = GUIUtility.hotControl == id;
            Slab.Box(new Rect(knobX - knobW / 2f, track.y - 3f, knobW, track.height + 6f),
                grabbed ? Lighten(KnobFace, -0.20f) : Mouse.IsOver(hit)
                    ? Lighten(KnobFace, -0.08f) : KnobFace,
                Clear);
            return value;
        }

    }

    public abstract class SlopWidgets : SlopButtons
    {
        // One measurement rule for every action row, kept beside the layout helpers rather
        // than duplicated by individual windows.
        public static float BtnW(string label, float floor) =>
            Mathf.Max(Wide(label) + ButtonPadX * 2f, floor);

        // Form buttons use their content width; callers that own fixed geometry (row action
        // clusters, key cells, footer bars) continue to use Button(Rect, ...).
        public static bool Button(Listing_Standard l, string label, Btn kind = Btn.Default,
                                  bool on = true)
        {
            var r = l.GetRect(BtnH);
            r.width = Mathf.Min(r.width, BtnW(label, ButtonMinW));
            return Button(r, label, kind, on);
        }

        // An icon that answers to a press, in the chrome's own rectangular hover surface.
        //
        // `tint` is the icon's color at rest - a disabled errand hands over a faded one -
        // and it goes to full white under the mouse.
        public static bool IconButton(Rect r, Texture2D icon, Color tint, bool on = true)
        {
            return IconButton(r, icon, tint, IconInset, on);
        }

        // A compact icon can keep a deliberate inset while sharing the same hover and press
        // treatment as a full-size chrome icon (the window close cross is the one case).
        public static bool IconButton(Rect r, Texture2D icon, Color tint, float inset,
                                      bool on = true)
        {
            bool over = on && Mouse.IsOver(r);
            bool held = over && Input.GetMouseButton(0);

            if (over) Slab.Fill(r, held ? BtnDown : BtnHover);

            var was = GUI.color;
            GUI.color = on ? (over ? Lead : tint) : Fade(tint, 0.5f);
            GUI.DrawTexture(r.ContractedBy(inset), icon);
            GUI.color = was;

            if (!on || !Widgets.ButtonInvisible(r)) return false;

            SoundDefOf.Click.PlayOneShotOnCamera();
            return true;
        }

        public static bool IconButton(Rect r, Texture2D icon, bool on = true) =>
            IconButton(r, icon, Name, on);

        public static void SectionHeading(Listing_Standard l, string text) =>
            SectionHeading(l.GetRect(RowH), text);

        public static void Note(Listing_Standard l, string text)
        {
            GUI.color = Dim;
            l.Label(text);
            GUI.color = Color.white;
        }

        public struct Bar
        {
            Rect _r;
            float _left, _right;

            public Bar(Rect r) { _r = r; _left = 0f; _right = 0f; }

            public bool Left(string label, Btn kind = Btn.Default, bool on = true)
            {
                float w = Wide(label);
                var at = new Rect(_r.x + _left, _r.y, w, SlopWidgets.BtnH);
                _left += w + SlopWidgets.GapS;
                return SlopWidgets.Button(at, label, kind, on);
            }

            public bool Right(string label, Btn kind = Btn.Default, bool on = true)
            {
                float w = Wide(label);
                var at = new Rect(_r.xMax - _right - w, _r.y, w, SlopWidgets.BtnH);
                _right += w + SlopWidgets.GapS;
                return SlopWidgets.Button(at, label, kind, on);
            }

            public Rect Rest()
            {
                float x = _r.x + _left;
                return new Rect(x, _r.y,
                    Mathf.Max(_r.xMax - _right - SlopWidgets.GapS - x, 0f),
                    SlopWidgets.BtnH);
            }

            // Measured at Small whatever the caller left the font at. [BtnH] is a fixed
            // height cut for that face, so a width taken against another one gives a box
            // that does not match its own row.
            static float Wide(string label)
            {
                var was = Verse.Text.Font;
                Verse.Text.Font = GameFont.Small;
                float w = SlopWidgets.BtnW(label, SlopWidgets.ButtonMinW);
                Verse.Text.Font = was;
                return w;
            }
        }

        public const string Unreachable =
            "Daemon unreachable. Is slopd running?  systemctl --user status slopd";

        public static void Fail(string msg) =>
            Messages.Message($"SlopWorld: {msg}", MessageTypeDefOf.RejectInput, false);

        // A checked menu row wears the same box a settings page does, before the label
        // rather than after it: `SlopMenu` reads `extraPartRightJustified` and puts the
        // part on the left when it is false. One checkbox everywhere, so a tick means the
        // same thing wherever it is read.
        public static FloatMenuOption MenuToggle(string label, bool on, Action act)
        {
            var opt = new FloatMenuOption(label, act, MenuOptionPriority.Default, null, null,
                TickColW, r => DrawTick(r, on));
            opt.extraPartRightJustified = false;
            return opt;
        }

        // OS font lists often expose a whole foundry under one leading word: Noto alone can
        // account for dozens of faces. Keep those families behind one menu while leaving
        // names with no siblings as one-click choices.
        public static IEnumerable<FloatMenuOption> GroupedFontOptions(IEnumerable<string> names,
                                                                       Action<string> choose)
        {
            var groups = (names ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrEmpty(name))
                .GroupBy(FontFamily, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var faces = group.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
                if (faces.Count == 1)
                {
                    yield return FontOption(faces[0], choose);
                    continue;
                }

                var family = group.Key;
                yield return new SlopSubmenu(family, () => faces
                    .Select(name => FontOption(name, choose))
                    .ToList());
            }
        }

        static string FontFamily(string name)
        {
            int space = name.IndexOf(' ');
            return space > 0 ? name.Substring(0, space) : name;
        }

        static FloatMenuOption FontOption(string name, Action<string> choose) =>
            new FloatMenuOption(name, () => choose(name));

        static bool DrawTick(Rect r, bool on)
        {
            TickBox(r, on);
            // extraPartOnGUI's return means the extra part handled the click; the row does.
            return false;
        }

        public static void Header(Rect rect, string title, SessionHub hub)
        {
            Title(rect, title);
            Status(new Rect(rect.x, rect.y, rect.width, HeaderH), hub);
        }

        static void Status(Rect line, SessionHub hub)
        {
            string text = $"{SlopClient.BaseUrl} - {hub.Status}";
            float w = Wide(text);

            float h = RowH;
            var badge = new Rect(line.xMax - (w + StatusMarker + GapS * 3f),
                line.y + (line.height - h) / 2f,
                w + StatusMarker + GapS * 3f, h);
            Slab.Box(badge, Well, Edge);

            var marker = new Rect(badge.x + GapS, badge.y + (h - StatusMarker) / 2f,
                StatusMarker, StatusMarker);
            Slab.Fill(marker, hub.Online ? Yes : Bad);

            GUI.color = Dim;
            RowLabel(new Rect(marker.xMax + GapS, badge.y, w + 2f, h), text);
            GUI.color = Color.white;
        }

        public static void SectionHeading(Rect r, string text)
        {
            var was = Text.Font;
            Text.Font = GameFont.Small;

            float w = Wide(text);
            GUI.color = Faint;
            RowLabel(r, text);
            GUI.color = Color.white;

            Text.Font = was;

            float x = r.x + w + GapS;
            if (x < r.xMax)
                Slab.Hairline(new Rect(x, r.y + r.height / 2f, r.xMax - x, 1f), Edge);
        }

        public static void PageCaption(Rect page, string text)
        {
            GUI.color = Dim;
            RowLabel(new Rect(page.x, page.y, page.width, RowH), text);
            GUI.color = Color.white;
        }

        // Settings pages leave room for their footer, but otherwise use the tab's whole
        // content area. The old caption and card chrome are intentionally gone.
        public static Rect PageBody(Rect page) =>
            new Rect(page.x, page.y, page.width, page.height - BtnH - GapS);

        public static Rect FooterBar(Rect rect) =>
            new Rect(rect.x, rect.yMax - BtnH, rect.width, BtnH);

        public static void Title(Rect rect, string text)
        {
            var line = new Rect(rect.x, rect.y, rect.width, HeaderH);

            Text.Font = GameFont.Medium;
            GUI.color = Lead;
            RowLabel(line, text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Slab.Hairline(new Rect(rect.x, line.yMax, rect.width, 1f), Edge);
        }

        public static void RowChrome(Rect r)
        {
            Slab.Fill(r, RowBg);
            if (Mouse.IsOver(r)) Slab.Fill(r, Hover);
        }

        public static bool HoverRow(Rect r)
        {
            bool on = ColonistBarStrip.SidebarHover(r);
            if (on) Slab.Fill(r, Hover);
            return on;
        }

        public static string PathList(Rect r, string name, string label, string text)
        {
            float h = RowH;
            SectionHeading(new Rect(r.x, r.y, r.width, h), label);
            var box = new Rect(r.x, r.y + h + GapXS, r.width,
                Mathf.Max(r.height - h - GapXS, 40f));
            return Area(box, name, text);
        }

        public static string FreeName(string name, IEnumerable<string> taken, string fallback)
        {
            string stem = name ?? "";
            while (stem.Length > 0 && char.IsDigit(stem[stem.Length - 1]))
                stem = stem.Substring(0, stem.Length - 1);
            stem = stem.TrimEnd(' ', '-', '_');
            if (stem.Length == 0) stem = name ?? fallback;

            var used = taken.ToList();
            for (int n = 2; n <= 99; n++)
            {
                string candidate = stem + "-" + n;
                if (!used.Contains(candidate)) return candidate;
            }
            return stem;
        }

        public static void DrawRail<T>(Rect r, (string label, T tab)[] tabs, ref T active)
        {
            float y = r.y;
            foreach (var tab in tabs)
                y = RailTab(r, y, tab.label, tab.tab, ref active);
        }

        static float RailTab<T>(Rect r, float y, string label, T tab, ref T active)
        {
            if (Button(new Rect(r.x, y, r.width, BtnH), label,
                    EqualityComparer<T>.Default.Equals(active, tab) ? Btn.Primary : Btn.Ghost))
                active = tab;
            return y + BtnH + GapS;
        }
    }

    public abstract class SlopListView<T> : IContentView
    {
        readonly SmoothScroll _scroll = new SmoothScroll();

        public abstract string Title { get; }

        protected abstract float RowH { get; }

        protected abstract string EmptyNote { get; }

        protected abstract IEnumerable<T> Rows { get; }

        protected abstract void DrawRow(Rect r, T item);

        protected abstract void DoFooter(Rect bar, SessionHub hub);

        public virtual void Opened() { }

        public virtual void Closed() { }

        public void Draw(Rect rect)
        {
            var hub = SessionHub.Instance;

            SlopWidgets.Header(rect, Title, hub);

            float top = rect.y + SlopWidgets.HeaderH + SlopWidgets.GapS;
            float foot = SlopWidgets.BtnH + SlopWidgets.GapS;
            DrawList(new Rect(rect.x, top, rect.width, rect.yMax - foot - top), hub);

            DoFooter(new Rect(rect.x, rect.yMax - SlopWidgets.BtnH, rect.width,
                SlopWidgets.BtnH), hub);
        }

        void DrawList(Rect rect, SessionHub hub)
        {
            var items = Rows.ToList();
            var view = new Rect(0f, 0f, rect.width - SlopWidgets.ScrollbarW,
                items.Count * RowH + SlopWidgets.GapXS);

            _scroll.Begin(rect, view);

            if (items.Count == 0)
            {
                GUI.color = SlopWidgets.Dim;
                string note = hub.Online ? EmptyNote : SlopWidgets.Unreachable;
                Widgets.Label(
                    new Rect(SlopWidgets.GapXS, SlopWidgets.GapS,
                        view.width - SlopWidgets.GapS,
                        Text.CalcHeight(note, view.width - SlopWidgets.GapS)),
                    note);
                GUI.color = Color.white;
            }

            float y = 0f;
            foreach (var item in items)
            {
                DrawRow(new Rect(0f, y, view.width, RowH - SlopWidgets.GapXS), item);
                y += RowH;
            }

            _scroll.End();
        }
    }
}
