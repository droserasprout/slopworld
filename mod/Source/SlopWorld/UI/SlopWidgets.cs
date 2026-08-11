using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    // SlopWorld's chrome is a dark instrument panel: squared frames, a blue signal colour,
    // and a small set of opaque surfaces. The map can stay visible behind the panel,
    // but a control never borrows its fill from the colony underneath it.
    //
    // The geometry follows one rhythm. Four pixels separates a label from its rule, eight
    // separates controls, and sixteen/twenty-four separate groups and sections. [Slab] owns
    // all fills and edges so corners stay square and every edge lands on the screen grid.
    public static class SlopWidgets
    {
        // ---- Surfaces and semantic colours. These are named for SlopWorld's jobs rather
        // than for a borrowed toolkit's widgets.

        public static readonly Color Accent = new Color(0.208f, 0.518f, 0.894f);      // signal blue
        public static readonly Color Destructive = new Color(0.753f, 0.110f, 0.157f); // hard red

        public static readonly Color WindowBg = new Color(0.141f, 0.141f, 0.141f);
        public static readonly Color ViewBg = new Color(0.118f, 0.118f, 0.118f);
        public static readonly Color PopoverBg = new Color(0.220f, 0.220f, 0.220f);

        public static readonly Color Lead = new Color(1f, 1f, 1f, 1.00f);
        public static readonly Color Name = new Color(1f, 1f, 1f, 0.80f);
        public static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
        public static readonly Color Faint = new Color(1f, 1f, 1f, 0.42f);
        public static readonly Color Off = new Color(1f, 1f, 1f, 0.30f);

        public static readonly Color Bad = new Color(1f, 0.482f, 0.388f);
        public static readonly Color Warn = new Color(0.973f, 0.776f, 0.208f);
        public static readonly Color Global = new Color(0.79f, 0.72f, 0.91f);

        // Panel is the one surface allowed to show a trace of the map beneath it. All
        // controls and popovers use the opaque surfaces above.
        public static readonly Color Panel = new Color(0.141f, 0.141f, 0.141f, 0.96f);
        public static readonly Color OfflineBg = new Color(0.42f, 0.12f, 0.10f, 0.92f);

        // A wash under text that stands on the map, where there is no surface to put it on.
        public static readonly Color Scrim = new Color(0f, 0f, 0f, 0.55f);

        // Structural lines are a restrained white wash. EdgeLit is reserved for the resize
        // grip and other places where the pointer is actively on the structure.
        public static readonly Color Edge = new Color(1f, 1f, 1f, 0.14f);
        public static readonly Color EdgeLit = new Color(0.55f, 0.60f, 0.70f, 0.90f);
        public static readonly Color ScrollTrough = new Color(1f, 1f, 1f, 0.04f);
        public static readonly Color ScrollThumb = new Color(1f, 1f, 1f, 0.28f);
        public static readonly Color ScrollThumbHover = new Color(1f, 1f, 1f, 0.42f);
        public static readonly Color ScrollThumbHeld = new Color(1f, 1f, 1f, 0.55f);

        public static readonly Color Well = ViewBg;

        // One green for "this is up" and "this is on"; Yes is the name the forms ask for it
        // by, and the status marker is the same colour saying the same thing.
        static readonly Color Online = new Color(0.345f, 0.769f, 0.294f);
        public static readonly Color Yes = Online;

        public static readonly Color RowBg = new Color(1f, 1f, 1f, 0.03f);
        public static readonly Color RowOn = new Color(1f, 1f, 1f, 0.10f);
        public static readonly Color Sel = new Color(0.208f, 0.518f, 0.894f, 0.35f);
        public static readonly Color Hover = new Color(1f, 1f, 1f, 0.06f);

        public static readonly Color StateWorking = new Color(0.45f, 0.75f, 0.95f);
        public static readonly Color StateWaiting = new Color(0.98f, 0.80f, 0.30f);
        public static readonly Color StateIdle = new Color(0.60f, 0.62f, 0.64f);
        public static readonly Color StateDown = new Color(0.85f, 0.35f, 0.35f);
        public static readonly Color Info = StateWorking;

        public enum Btn
        {
            Default,   // the ordinary press: Reload, Browse, Edit.
            Primary,   // what the window was opened to do. One per bar, or it means nothing.
            Danger,    // takes something away. Still asks first; this is so it is read first.
            Ghost,     // there, but not competing - a press beside a press that matters more.
        }

        static readonly Color BtnEdge = Edge;

        static readonly Color BtnFace = new Color(1f, 1f, 1f, 0.10f);
        static readonly Color BtnHover = new Color(1f, 1f, 1f, 0.15f);
        static readonly Color BtnDown = new Color(1f, 1f, 1f, 0.30f);

        // A ghost button has no face at rest; its rectangular hit area appears on hover.
        static readonly Color GhostFace = new Color(1f, 1f, 1f, 0f);

        static readonly Color FocusRing = Accent;

        static readonly Color KnobFace = new Color(1f, 1f, 1f);

        static readonly Color PrimeFace = Accent;
        static readonly Color DangerFace = Destructive;

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
        public const float MenuPadY = 4f;
        public const float StatusMarker = 8f;

        public static float RowBtnH => Mathf.Max(LineH + GapXS, 22f);
        public static float MenuRowH => Mathf.Max(LineH + GapS, 26f);

        public static float BtnW(string label, float floor) =>
            Mathf.Max(Wide(label) + ButtonPadX * 2f, floor);

        public const float GapXS = 4f;   // a label and the box it names
        public const float GapS = 8f;    // one control and the next
        public const float GapM = 16f;   // one group of controls and the next
        public const float GapL = 24f;   // one section and the next

        public static bool Button(Rect r, string label, Btn kind = Btn.Default, bool on = true)
        {
            bool over = on && Mouse.IsOver(r);
            bool held = over && Input.GetMouseButton(0);

            // A button changes face, never shape. Pressing a rectangular instrument should not
            // make the layout jump underneath the pointer.
            Color face, text;
            switch (kind)
            {
                case Btn.Primary:
                    face = Step(PrimeFace, over, held); text = Color.white; break;
                case Btn.Danger:
                    face = Step(DangerFace, over, held); text = Color.white; break;
                case Btn.Ghost:
                    face = held ? BtnDown : over ? BtnFace : GhostFace;
                    text = over ? Lead : Name;
                    break;
                default:
                    face = held ? BtnDown : over ? BtnHover : BtnFace;
                    text = Lead; break;
            }

            // Insensitive controls keep their geometry and lose contrast together.
            if (!on)
            {
                face = Fade(face, 0.5f);
                text = Fade(text, 0.5f);
            }

            // A solid accent button has no border; a flat one has none until it is touched.
            bool solid = kind == Btn.Primary || kind == Btn.Danger;
            var edge = solid || (kind == Btn.Ghost && !over && !held)
                ? Clear
                : on ? BtnEdge : Fade(BtnEdge, 0.5f);

            Slab.Box(r, face, edge);

            var wasAnchor = Text.Anchor;
            var wasColor = GUI.color;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = text;
            Widgets.Label(r, label);
            Text.Anchor = wasAnchor;
            GUI.color = wasColor;

            if (!on || !Widgets.ButtonInvisible(r)) return false;

            SoundDefOf.Click.PlayOneShotOnCamera();
            return true;
        }

        public static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

        static Color Lighten(Color c, float by)
        {
            var to = by >= 0f ? Color.white : Color.black;
            float t = Mathf.Abs(by);
            return new Color(Mathf.Lerp(c.r, to.r, t), Mathf.Lerp(c.g, to.g, t),
                Mathf.Lerp(c.b, to.b, t), c.a);
        }

        // Opacity is used only for disabled or overlaid states; base surfaces stay opaque.
        static Color Fade(Color c, float by) =>
            new Color(c.r, c.g, c.b, c.a * by);

        // Accent and destructive buttons use a lightness step; ordinary buttons use the
        // same translucent white faces over every shared surface.
        static Color Step(Color c, bool over, bool held) =>
            held ? Lighten(c, -0.15f) : over ? Lighten(c, 0.10f) : c;

        public static float LineH => LineHOf(GameFont.Small);

        public static float FieldH => LineH + GapS;
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

        public static void RowLabel(Rect r, string text)
        {
            // Truncate measures incorrectly while wrapping is enabled.
            bool wrap = Verse.Text.WordWrap;
            Verse.Text.WordWrap = false;
            Widgets.Label(r, (text ?? "").Truncate(Mathf.Max(1f, r.width)));
            Verse.Text.WordWrap = wrap;
        }

        public static string Field(Rect r, string name, string text, bool on = true)
        {
            bool focused = on && GUI.GetNameOfFocusedControl() == name;
            Slab.Box(r, Well, focused ? Accent : BtnEdge);
            // The one control in this file with real keyboard focus: the blue ring makes
            // the active rectangular entry legible across a dark form.
            if (focused) Slab.Ring(r, FocusRing);

            var inner = r.ContractedBy(FieldPadX, 0f);
            // Do not create a control that accepts input only to discard it next frame.
            if (!on) return Stated(inner, text, TextAnchor.MiddleLeft);

            GUI.SetNextControlName(name);
            return GUI.TextField(inner, text ?? "", Bare(Verse.Text.CurTextFieldStyle));
        }

        public static string Area(Rect r, string name, string text, bool on = true,
                                  bool frame = true)
        {
            bool focused = on && GUI.GetNameOfFocusedControl() == name;
            if (frame)
            {
                Slab.Box(r, Well, focused ? Accent : BtnEdge);
                if (focused) Slab.Ring(r, FocusRing);
            }

            var inner = frame ? r.ContractedBy(FieldPadX, FieldPadY * 2f) : r;
            if (!on) return Stated(inner, text, TextAnchor.UpperLeft);

            GUI.SetNextControlName(name);
            return GUI.TextArea(inner, text ?? "", Bare(Verse.Text.CurTextAreaStyle));
        }

        // The entry's frame on its own, for a caller drawing one box round more than one
        // thing - the command palette puts a prompt and an input inside a single entry.
        public static void FieldFrame(Rect r, bool focused)
        {
            Slab.Box(r, Well, focused ? Accent : BtnEdge);
            if (focused) Slab.Ring(r, FocusRing);
        }

        // The text field with no frame of its own, for the same caller.
        public static string BareField(Rect r, string name, string text)
        {
            GUI.SetNextControlName(name);
            return GUI.TextField(r, text ?? "", Bare(Verse.Text.CurTextFieldStyle));
        }

        static GUIStyle Bare(GUIStyle of)
        {
            var style = new GUIStyle(of) { normal = { background = null } };
            style.focused.background = null;
            style.hover.background = null;
            return style;
        }

        static string Stated(Rect r, string text, TextAnchor anchor)
        {
            var wasAnchor = Verse.Text.Anchor;
            Verse.Text.Anchor = anchor;
            GUI.color = Faint;
            Widgets.Label(r, text ?? "");
            GUI.color = Color.white;
            Verse.Text.Anchor = wasAnchor;
            return text;
        }

        // The box a checkbox wears, on its own and centred in the height it is given:
        // checked is solid signal blue with a tick, unchecked a square well with a light
        // edge. Drawn and not clicked - every caller has a row that is the hit target
        // already - so the box goes wherever a tick belongs, in a settings page, a menu
        // and the palette's sub list alike. It returns the square it drew, for a caller
        // laying a label out beside it; [TickColW] is what a column of them costs.
        public static float TickW => Mathf.Round(LineH * 0.8f);
        public static float TickColW => TickW + GapS;

        public static Rect TickBox(Rect r, bool on, bool locked = false)
        {
            float size = Mathf.Min(TickW, r.height - 2f);
            var box = new Rect(r.x, r.y + (r.height - size) / 2f, size, size);

            var face = on ? PrimeFace : Well;
            var edge = on ? Clear : BtnEdge;
            if (locked) { face = Fade(face, 0.5f); edge = Fade(edge, 0.5f); }
            Slab.Box(box, face, edge);
            if (on)
            {
                GUI.color = locked ? Faint : Color.white;
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

            var wasAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = locked ? Faint : warn ? Warn : over ? Lead : Name;
            Widgets.Label(new Rect(box.xMax + 8f, r.y, r.xMax - box.xMax - 8f, r.height), label);
            Text.Anchor = wasAnchor;
            GUI.color = Color.white;

            if (locked || !Widgets.ButtonInvisible(r)) return on;

            SoundDefOf.Click.PlayOneShotOnCamera();
            return !on;
        }

        public static string Field(Listing_Standard l, string name, string text, bool on = true) =>
            Field(l.GetRect(FieldH), name, text, on);

        public static bool Checkbox(Listing_Standard l, string label, bool on, string tip = null) =>
            Checkbox(l.GetRect(RowH), label, on, tip);

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
                                   float min, float max, string readout, string tip = null)
        {
            var r = l.GetRect(RowH + GapS);
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);

            float span = max - min;
            float at = span <= 0f ? 0f : Mathf.Clamp01((value - min) / span);
            return min + Track(r, label, at, readout) * span;
        }

        static float Track(Rect r, string label, float value, string readout)
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
            var oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(valueRect, readout);
            Text.Anchor = oldAnchor;
            GUI.color = Color.white;

            int id = GUIUtility.GetControlID(FocusType.Passive, track);
            var e = Event.current;
            var hit = new Rect(track.x - knobW / 2f, r.y, track.width + knobW, RowH);
            if (e.type == EventType.MouseDown && e.button == 0 && hit.Contains(e.mousePosition))
            {
                GUIUtility.hotControl = id;
                e.Use();
            }
            if (GUIUtility.hotControl == id)
            {
                if (e.type == EventType.MouseDrag || e.type == EventType.MouseDown)
                {
                    value = Mathf.Clamp01(Mathf.InverseLerp(track.x, track.xMax,
                        e.mousePosition.x));
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && e.button == 0)
                {
                    value = Mathf.Clamp01(Mathf.InverseLerp(track.x, track.xMax,
                        e.mousePosition.x));
                    GUIUtility.hotControl = 0;
                    e.Use();
                }
            }

            Slab.Box(track, Well, BtnEdge);
            var fill = new Rect(track.x, track.y, track.width * Mathf.Clamp01(value), track.height);
            if (fill.width > 0f) Slab.Fill(fill, PrimeFace);

            // The slider's square light knob makes a row of levels readable at a glance.
            float knobX = Mathf.Lerp(track.x, track.xMax, Mathf.Clamp01(value));
            bool grabbed = GUIUtility.hotControl == id;
            Slab.Box(new Rect(knobX - knobW / 2f, track.y - 3f, knobW, track.height + 6f),
                grabbed ? Lighten(KnobFace, -0.20f) : Mouse.IsOver(hit)
                    ? Lighten(KnobFace, -0.08f) : KnobFace,
                Clear);
            return value;
        }

        // A page's ground: an opaque rectangular card with a structural edge.
        public static void Card(Rect r) => Slab.Box(r, WindowBg, Edge);

        // An icon that answers to a press, in the chrome's own rectangular hover surface.
        //
        // `tint` is the icon's colour at rest - a disabled errand hands over a faded one -
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
            var wasAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;

            string text = $"{SlopClient.BaseUrl} - {hub.Status}";
            float w = Wide(text);

            float h = RowH;
            var badge = new Rect(line.xMax - (w + StatusMarker + GapS * 3f),
                line.y + (line.height - h) / 2f,
                w + StatusMarker + GapS * 3f, h);
            Slab.Box(badge, Well, Edge);

            var marker = new Rect(badge.x + GapS, badge.y + (h - StatusMarker) / 2f,
                StatusMarker, StatusMarker);
            Slab.Fill(marker, hub.Online ? Online : Bad);

            GUI.color = Dim;
            Widgets.Label(new Rect(marker.xMax + GapS, badge.y, w + 2f, h), text);
            GUI.color = Color.white;
            Text.Anchor = wasAnchor;
        }

        public static void SectionHeading(Rect r, string text)
        {
            var wasAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            var was = Text.Font;
            Text.Font = GameFont.Small;

            float w = Wide(text);
            GUI.color = Faint;
            Widgets.Label(r, text);
            GUI.color = Color.white;

            Text.Font = was;
            Text.Anchor = wasAnchor;

            float x = r.x + w + GapS;
            if (x < r.xMax)
                Slab.Hairline(new Rect(x, r.y + r.height / 2f, r.xMax - x, 1f), Edge);
        }

        public static void PageCaption(Rect page, string text)
        {
            GUI.color = Dim;
            Widgets.Label(new Rect(page.x, page.y, page.width, RowH), text);
            GUI.color = Color.white;
        }

        public static Rect PageBody(Rect page)
        {
            float top = page.y + RowH + GapXS;
            return new Rect(page.x, top, page.width, page.yMax - BtnH - GapS - top);
        }

        public static Rect FooterBar(Rect rect) =>
            new Rect(rect.x, rect.yMax - BtnH, rect.width, BtnH);

        public static void Title(Rect rect, string text)
        {
            var line = new Rect(rect.x, rect.y, rect.width, HeaderH);
            var wasAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;

            Text.Font = GameFont.Medium;
            GUI.color = Lead;
            Widgets.Label(line, text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Text.Anchor = wasAnchor;
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
