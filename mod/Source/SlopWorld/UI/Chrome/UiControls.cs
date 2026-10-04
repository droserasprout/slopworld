using System;
using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    public abstract class UiButtons : UiText
    {
        protected const float SliderValueWidth = 46f;
        protected const float SliderKnobWidth = 12f;
        protected const float SliderLabelMinWidth = 120f;
        protected const float SliderLabelMaxShare = 0.42f;
        protected const float SliderTrackHeight = 8f;
        protected const float SliderKnobVerticalInset = 3f;

        // Custom rows draw their own face and use RimWorld only for its event semantics. Keep
        // that adapter in one place so row callers do not accidentally add native chrome or
        // reorder hit testing around their hover/selection pass.
        public static bool RowButton(Rect r, bool on = true) =>
            on && Widgets.ButtonInvisible(r);

        public static bool Button(Rect r, string label, Btn kind = Btn.Default, bool on = true)
        {
            using (WidgetState.Save()) return ButtonCore(r, label, kind, on);
        }

        static bool ButtonCore(Rect r, string label, Btn kind, bool on)
        {
            bool over = on && Mouse.IsOver(r);
            bool held = over && Input.GetMouseButton(0);
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

            using (WidgetState.Save())
            {
                GUI.color = text;
                RowLabel(r, label, TextAnchor.MiddleCenter);
            }

            if (!RowButton(r, on)) return false;
            SoundDefOf.Click.PlayOneShotOnCamera();
            return true;
        }

        public static void ButtonBackground(Rect r, Btn kind, bool on, bool over, bool held)
        {
            ButtonBackground(r, kind, on, over, held, Well);
        }

        // Session gizmos sit over the map beside the sidebar. Layer the hover wash over an opaque
        // well. Using BtnHover as the whole face would make the map show through as soon as the
        // pointer entered the action strip.
        public static void ActionButtonBackground(Rect r, Btn kind, bool on, bool over,
                                                  bool held)
        {
            ButtonBackground(r, kind, on, over, held, Well, true);
        }

        static void ButtonBackground(Rect r, Btn kind, bool on, bool over, bool held,
                                     Color defaultFace, bool opaqueHover = false)
        {
            Color face;
            if (opaqueHover && kind == Btn.Default)
            {
                Slab.Fill(r, on ? defaultFace : Fade(defaultFace, 0.5f));
                if (on && over) Slab.Fill(r, held ? BtnDown : BtnHover);
                face = Clear;
            }
            else
            {
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
            }

            bool solid = kind == Btn.Primary || kind == Btn.Danger;
            var edge = solid || (kind == Btn.Ghost && !over && !held)
                ? Clear
                : on ? BtnEdge : Fade(BtnEdge, 0.5f);

            Slab.Box(r, face, edge);
        }
    }

    public abstract class UiControls : UiButtons
    {
        // Draw the checkbox indicator only. The row owns hit testing. Return its reserved width
        // so callers can place the adjacent label.
        public static float TickW => Mathf.Round(LineH * 0.8f);
        public static float TickColW => TickW + GapS;

        public static Rect TickBox(Rect r, bool on, bool locked = false)
        {
            using (WidgetState.Save()) return TickBoxCore(r, on, locked);
        }

        static Rect TickBoxCore(Rect r, bool on, bool locked)
        {
            float size = Mathf.Min(TickW, r.height - 2f);
            var box = new Rect(r.x, r.y + (r.height - size) / 2f, size, size);
            var face = on ? CheckFace : Well;
            var edge = on ? Clear : BtnEdge;
            if (locked) { face = Fade(face, 0.5f); edge = Fade(edge, 0.5f); }
            Slab.Box(box, face, edge);
            if (on)
            {
                using (WidgetState.Save())
                {
                    GUI.color = locked ? Faint : UIScheme.TextOn(face);
                    GUI.DrawTexture(box.ContractedBy(IconInset), Icons.Check);
                }
            }
            return box;
        }

        public static bool Checkbox(Rect r, string label, bool on, string tip = null,
                                    bool locked = false, bool warn = false)
        {
            using (WidgetState.Save())
                return CheckboxCore(r, label, on, tip, locked, warn);
        }

        static bool CheckboxCore(Rect r, string label, bool on, string tip,
                                 bool locked, bool warn)
        {
            bool over = !locked && Mouse.IsOver(r);
            if (over) Slab.Fill(r, Hover);
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            var box = TickBox(new Rect(r.x + 1f, r.y, TickW, r.height), on, locked);
            using (WidgetState.Save())
            {
                GUI.color = locked ? Faint : warn ? Warn : over ? Lead : Name;
                RowLabel(new Rect(box.xMax + GapS, r.y, r.xMax - box.xMax - GapS, r.height), label);
            }
            if (locked || !RowButton(r)) return on;
            SoundDefOf.Click.PlayOneShotOnCamera();
            return !on;
        }

        // A caption and its field are separate listing rows. Keep the shared label-to-control gap
        // here, and leave the same standard gap after every listing control. Therefore, forms do
        // not have to add either margin by hand.
        public static Rect FieldRect(Listing_Standard l)
        {
            // Label already advanced by verticalSpacing. Selectors draw caption and
            // control in one rect, so adding the full gap here makes fields looser.
            l.Gap(GapXS - l.verticalSpacing);
            return l.GetRect(FieldH);
        }

        public static string Field(Listing_Standard l, string name, string text, bool on = true,
                                   string defaultValue = null)
        {
            string value = Field(FieldRect(l), name, text, on, defaultValue);
            l.Gap(GapS);
            return value;
        }

        public static string Area(Listing_Standard l, float height, string name, string text,
                                  bool on = true, bool frame = true, string defaultValue = null,
                                  UiAreaResize resize = null)
        {
            l.Gap(GapXS - l.verticalSpacing);
            string value = Area(l.GetRect(resize == null ? height : AreaHeight(l.ColumnWidth, text, resize, on)), name, text, on, frame,
                defaultValue, resize);
            l.Gap(GapS);
            return value;
        }

        public static bool SetSetting<T>(ModSettings settings, ref T field, T value)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            settings.MarkDirty();
            return true;
        }

        public static void CheckboxSetting(Listing_Standard l, string label, ModSettings settings,
            ref bool field, string tip = null) =>
            SetSetting(settings, ref field, Checkbox(l, label, field, tip));

        public static bool SliderSetting(Listing_Standard l, string label, ModSettings settings,
            ref int field, int min, int max) =>
            SetSetting(settings, ref field, Mathf.RoundToInt(Slider(l, label, field,
                min, max, field.ToString(CultureInfo.CurrentCulture))));

        public static bool Checkbox(Listing_Standard l, string label, bool on, string tip = null,
                                    bool locked = false)
        {
            bool value = Checkbox(l.GetRect(RowH), label, on, tip, locked);
            l.Gap(GapS);
            return value;
        }

        // A dropdown caret. Use the same disclosure textures as the other foldables: a
        // closed control points toward its contents, while an open one points down.
        static void Chevron(Rect r, Color c, bool open)
        {
            var wasColor = GUI.color;
            try
            {
                GUI.color = c;
                GUI.DrawTexture(r, open ? TexButton.Collapse : TexButton.Reveal);
            }
            finally { GUI.color = wasColor; }
        }

        public static bool Select(Rect r, string caption, string value, out Rect box,
                                  string tip = null, bool on = true, bool open = false,
                                  float forcedWidth = 0f)
        {
            using (WidgetState.Save())
                return SelectCore(r, caption, value, out box, tip, on, open, forcedWidth);
        }

        static bool SelectCore(Rect r, string caption, string value, out Rect box,
                               string tip, bool on, bool open, float forcedWidth)
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
            bool shownOpen = open || UiMenu.IsSelectorOpenAt(MenuAt(box));

            using (WidgetState.Save())
            {
                GUI.color = on ? Name : Fade(Name, 0.5f);
                RowLabel(label, caption);
            }
            bool over = on && Mouse.IsOver(box);
            bool held = over && Input.GetMouseButton(0);
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            var face = !on ? Fade(Well, 0.5f) : held ? BtnDown : over ? BtnHover : Well;
            Slab.Box(box, face, on ? BtnEdge : Fade(BtnEdge, 0.5f));
            if (shownOpen) Slab.Ring(box, Accent);

            float caretX = box.xMax - ButtonPadX - chevron;
            Slab.VHairline(new Rect(caretX - GapS, box.y + GapXS, 1f, box.height - GapXS * 2f),
                           on ? BtnEdge : Fade(BtnEdge, 0.5f));
            Chevron(new Rect(caretX, box.y + (box.height - chevron) / 2f, chevron, chevron),
                    !on ? Fade(Faint, 0.5f) : over ? Accent : Faint, shownOpen);

            float textX = box.x + ButtonPadX;
            using (WidgetState.Save())
            {
                GUI.color = !on ? Fade(Lead, 0.5f) : over ? Lead : Name;
                RowLabel(new Rect(textX, box.y, Mathf.Max(0f, caretX - GapS - textX), box.height),
                    value);
            }
            if (!RowButton(box, on)) return false;
            SoundDefOf.Click.PlayOneShotOnCamera();
            return true;
        }

        public static bool Select(Rect r, string caption, string value,
                                  IEnumerable<FloatMenuOption> choices, out Rect box,
                                  string tip = null, bool on = true, bool open = false,
                                  Action<UiMenu> openMenu = null) =>
            UiSelector.Draw(r, caption, value, choices, out box, tip, on, open, openMenu);

        public static bool Select(Rect r, string caption, string value,
                                  IEnumerable<SelectorOption> choices, out Rect box,
                                  string tip = null, bool on = true, bool open = false,
                                  Action<UiMenu> openMenu = null) =>
            UiSelector.Draw(r, caption, value, choices, out box, tip, on, open, openMenu);

        public static bool Select(Listing_Standard l, string caption, string value,
                                  out Rect box, string tip = null, bool on = true)
        {
            bool pressed = Select(l.GetRect(LineH + GapXS + CompactH), caption, value,
                out box, tip, on);
            l.Gap(GapS);
            return pressed;
        }

        public static bool Select(Listing_Standard l, string caption, string value,
                                  IEnumerable<FloatMenuOption> choices, out Rect box,
                                  string tip = null, bool on = true, bool open = false,
                                  Action<UiMenu> openMenu = null) =>
            UiSelector.Draw(l, caption, value, choices, out box, tip, on, open, openMenu);

        public static bool Select(Listing_Standard l, string caption, string value,
                                  IEnumerable<SelectorOption> choices, out Rect box,
                                  string tip = null, bool on = true, bool open = false,
                                  Action<UiMenu> openMenu = null) =>
            UiSelector.Draw(l, caption, value, choices, out box, tip, on, open, openMenu);

        public static Vector2 MenuAt(Rect r) =>
            UI.GUIToScreenPoint(new Vector2(r.x, r.yMax));

        public static float Slider(Listing_Standard l, string label, float value,
                                   string tip = null) =>
            Slider(l, label, value, 0f, 1f,
                Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%", out _, out _, tip);

        public static float Slider(Listing_Standard l, string label, float value,
                                   float min, float max, string readout, string tip = null) =>
            Slider(l, label, value, min, max, readout, out _, out _, tip);

        public static float Slider(Listing_Standard l, string label, float value,
                                   float min, float max, string readout, out bool held,
                                   string tip = null) =>
            Slider(l, label, value, min, max, readout, out held, out _, tip);

        public static float Slider(Listing_Standard l, string label, float value,
                                   float min, float max, string readout, out bool held,
                                   out bool released, string tip = null)
        {
            using (WidgetState.Save())
                return SliderCore(l, label, value, min, max, readout, out held,
                    out released, tip);
        }

        static float SliderCore(Listing_Standard l, string label, float value,
                                float min, float max, string readout, out bool held,
                                out bool released, string tip)
        {
            var r = l.GetRect(RowH + GapS);
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            float span = max - min;
            float at = span <= 0f ? 0f : Mathf.Clamp01((value - min) / span);
            return min + Track(r, label, at, readout, out held, out released) * span;
        }

        sealed class SliderState
        {
            public float grab;
            public bool dragging;
        }

        static float Track(Rect r, string label, float value, string readout, out bool held,
                           out bool released)
        {
            float labelW = Mathf.Min(Mathf.Max(Wide(label) + GapM, SliderLabelMinWidth),
                r.width * SliderLabelMaxShare);
            var labelRect = new Rect(r.x, r.y, labelW, RowH);
            var valueRect = new Rect(r.xMax - SliderValueWidth, r.y, SliderValueWidth, RowH);
            var track = new Rect(labelRect.xMax + GapS,
                r.y + (RowH - SliderTrackHeight) / 2f,
                Mathf.Max(1f, valueRect.x - GapS - labelRect.xMax - GapS), SliderTrackHeight);

            using (WidgetState.Save())
            {
                GUI.color = Name;
                RowLabel(labelRect, label);
                RowLabel(valueRect, readout, TextAnchor.MiddleRight);
            }

            int id = GUIUtility.GetControlID(FocusType.Passive, track);
            var e = Event.current;
            var hit = new Rect(track.x - SliderKnobWidth / 2f, r.y,
                track.width + SliderKnobWidth, RowH);
            EventType mouseType = UiEvent.RawType(e);
            var state = GUIUtility.GetStateObject(typeof(SliderState), id) as SliderState;
            released = false;
            if (mouseType == EventType.MouseDown && e.button == 0 && hit.Contains(e.mousePosition))
            {
                float clickKnobX = Mathf.Lerp(track.x, track.xMax, Mathf.Clamp01(value));
                var knob = new Rect(clickKnobX - SliderKnobWidth / 2f,
                    track.y - SliderKnobVerticalInset, SliderKnobWidth,
                    track.height + SliderKnobVerticalInset * 2f);
                state.grab = knob.Contains(e.mousePosition)
                    ? e.mousePosition.x - clickKnobX
                    : 0f;
                state.dragging = true;
                GUIUtility.hotControl = id;
                value = Mathf.Clamp01(Mathf.InverseLerp(track.x, track.xMax,
                    e.mousePosition.x - state.grab));
                e.Use();
            }
            if (state.dragging)
            {
                if (mouseType == EventType.MouseDrag || mouseType == EventType.MouseDown)
                {
                    value = Mathf.Clamp01(Mathf.InverseLerp(track.x, track.xMax,
                        e.mousePosition.x - state.grab));
                    e.Use();
                }
                else if (mouseType == EventType.MouseUp && e.button == 0)
                {
                    value = Mathf.Clamp01(Mathf.InverseLerp(track.x, track.xMax,
                        e.mousePosition.x - state.grab));
                    if (GUIUtility.hotControl == id) GUIUtility.hotControl = 0;
                    state.dragging = false;
                    released = true;
                    e.Use();
                }
            }

            if (state.dragging && mouseType != EventType.MouseDown
                && mouseType != EventType.MouseDrag && mouseType != EventType.MouseUp
                && !Input.GetMouseButton(0))
            {
                if (GUIUtility.hotControl == id) GUIUtility.hotControl = 0;
                state.dragging = false;
                released = true;
            }

            Slab.Box(track, Well, BtnEdge);
            var fill = new Rect(track.x, track.y, track.width * Mathf.Clamp01(value), track.height);
            if (fill.width > 0f) Slab.Fill(fill, PrimeFace);
            float knobX = Mathf.Lerp(track.x, track.xMax, Mathf.Clamp01(value));
            bool grabbed = held = state.dragging;
            Slab.Box(new Rect(knobX - SliderKnobWidth / 2f,
                track.y - SliderKnobVerticalInset, SliderKnobWidth,
                track.height + SliderKnobVerticalInset * 2f),
                grabbed ? Lighten(KnobFace, -0.20f) : Mouse.IsOver(hit)
                    ? Lighten(KnobFace, -0.08f) : KnobFace,
                Clear);
            return value;
        }
    }
}
