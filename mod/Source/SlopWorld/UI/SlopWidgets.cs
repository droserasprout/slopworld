using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    // The palette is Adwaita's dark one, by its own names, squared off: the colours are the
    // library's and the geometry is [Slab]'s, which rounds nothing.
    //
    // Two rules carry most of it. **Surfaces are opaque and differ by lightness** - window,
    // view, headerbar and popover are four flat greys, not one grey at four alphas over the
    // game. **Controls are white over their surface** - a button is not a colour, it is the
    // surface plus ten percent white, which is why one face works on all four of them.
    public static class SlopWidgets
    {
        // ---- Adwaita, dark. The library's own values, hex in the comment so a drift from
        // upstream is one grep away.

        public static readonly Color Accent = new Color(0.208f, 0.518f, 0.894f);      // #3584e4
        public static readonly Color Destructive = new Color(0.753f, 0.110f, 0.157f); // #c01c28

        public static readonly Color WindowBg = new Color(0.141f, 0.141f, 0.141f);    // #242424
        public static readonly Color ViewBg = new Color(0.118f, 0.118f, 0.118f);      // #1e1e1e
        public static readonly Color PopoverBg = new Color(0.220f, 0.220f, 0.220f);   // #383838
        public static readonly Color HeaderBg = new Color(0.188f, 0.188f, 0.188f);    // #303030

        // ---- The text ramp. White at falling opacity rather than four greys: over any of
        // the surfaces above it comes out at the right lightness without being retuned per
        // surface, and Adwaita's own dim-label is exactly this (55%).

        public static readonly Color Lead = new Color(1f, 1f, 1f, 1.00f);
        public static readonly Color Name = new Color(1f, 1f, 1f, 0.80f);
        public static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
        public static readonly Color Faint = new Color(1f, 1f, 1f, 0.42f);
        public static readonly Color Off = new Color(1f, 1f, 1f, 0.30f);

        public static readonly Color Bad = new Color(1f, 0.482f, 0.388f);   // #ff7b63, dark error
        public static readonly Color Warn = new Color(0.973f, 0.776f, 0.208f);  // #f8c635
        public static readonly Color Global = new Color(0.79f, 0.72f, 0.91f);

        // The chrome's own ground - the top bar, the sidebar, the panels. Not quite opaque:
        // it is laid over a game that is still being played underneath it.
        public static readonly Color Panel = new Color(0.141f, 0.141f, 0.141f, 0.96f);

        // Adwaita's separator and border, which in dark is a low-alpha white rather than a
        // black line: a dark border on a dark surface is a groove, and the flat style has no
        // grooves in it.
        public static readonly Color Edge = new Color(1f, 1f, 1f, 0.14f);
        public static readonly Color EdgeLit = new Color(0.55f, 0.60f, 0.70f, 0.90f);

        // What a control sunk into a surface is filled with - an entry, the slider's track,
        // a list's frame. Adwaita's view background, opaque: a hole showing the map through
        // it is the one thing that never reads as a hole.
        public static readonly Color Well = ViewBg;

        static readonly Color Online = new Color(0.345f, 0.769f, 0.294f);  // #58c44b
        static readonly Color Offline = new Color(1f, 0.482f, 0.388f);

        public static readonly Color Yes = new Color(0.345f, 0.769f, 0.294f);

        public static readonly Color RowBg = new Color(1f, 1f, 1f, 0.03f);
        public static readonly Color RowOn = new Color(1f, 1f, 1f, 0.10f);

        // A selected row and a row under the mouse. Adwaita tints the selection with the
        // accent and the hover with plain white, which is the whole of how the two are told
        // apart when a list has one of each on it at the same time.
        public static readonly Color Sel = new Color(0.208f, 0.518f, 0.894f, 0.35f);
        public static readonly Color Hover = new Color(1f, 1f, 1f, 0.06f);

        public enum Btn
        {
            Default,   // the ordinary press: Reload, Browse, Edit.
            Primary,   // what the window was opened to do. One per bar, or it means nothing.
            Danger,    // takes something away. Still asks first; this is so it is read first.
            Ghost,     // there, but not competing - a press beside a press that matters more.
        }

        static readonly Color BtnEdge = Edge;

        // Adwaita's button is not a colour of its own: it is white over whatever it sits on,
        // at ten percent, and its hover and press are the same white at fifteen and thirty.
        // One face for every surface, which is the point - the same button reads correctly on
        // the window, on a card and in a popover without being told which it is on.
        static readonly Color BtnFace = new Color(1f, 1f, 1f, 0.10f);
        static readonly Color BtnHover = new Color(1f, 1f, 1f, 0.15f);
        static readonly Color BtnDown = new Color(1f, 1f, 1f, 0.30f);

        // A flat button: nothing at rest, the ordinary button's white once the mouse is on it.
        static readonly Color GhostFace = new Color(1f, 1f, 1f, 0f);

        static readonly Color FocusRing = Accent;

        static readonly Color KnobFace = new Color(1f, 1f, 1f);

        static readonly Color PrimeFace = Accent;
        static readonly Color DangerFace = Destructive;

        public const float BtnH = 30f;

        public static float RowBtnH => Mathf.Max(LineH + 2f, 22f);

        public static float BtnW(string label, float floor) =>
            Mathf.Max(Wide(label) + GapM, floor);

        public const float GapXS = 4f;   // a label and the box it names
        public const float GapS = 8f;    // one control and the next
        public const float GapM = 16f;   // one group of controls and the next
        public const float GapL = 24f;   // one section and the next

        public static bool Button(Rect r, string label, Btn kind = Btn.Default, bool on = true)
        {
            bool over = on && Mouse.IsOver(r);
            bool held = over && Input.GetMouseButton(0);

            // The state is the face and nothing else. There is no geometry in a press here -
            // Adwaita took the relief out of its buttons, so a button that moved or lost a
            // highlight would be the one thing on screen pretending to be a physical object.
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

            // Adwaita's insensitive: the control stays exactly where it is and everything in
            // it loses half its opacity. Not a darker face - these faces are white over the
            // surface, and darkening a translucent white makes it *lighter*.
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

        // Opacity, which is how Adwaita says "the same colour, less of it": a disabled label
        // and a disabled face are the enabled ones at half alpha, not separate colours.
        static Color Fade(Color c, float by) =>
            new Color(c.r, c.g, c.b, c.a * by);

        // A solid colour's hover and press. The accent buttons are opaque, so theirs is a
        // lightness step rather than the white overlay the flat ones use.
        static Color Step(Color c, bool over, bool held) =>
            held ? Lighten(c, -0.15f) : over ? Lighten(c, 0.10f) : c;

        public static float LineH => LineHOf(GameFont.Small);

        public static float FieldH => LineH + 8f;
        public static float RowH => LineH + 6f;

        public static float HeaderH => LineHOf(GameFont.Medium) + 8f;

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
            // The one control in this file that has real keyboard focus to show, IMGUI
            // tracking it by name. Adwaita rings the focused entry rather than only
            // recolouring its border, and the ring is what carries across a dark form.
            if (focused) Slab.Ring(r, FocusRing);

            var inner = r.ContractedBy(6f, 0f);
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

            var inner = frame ? r.ContractedBy(6f, 4f) : r;
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

        public static bool Checkbox(Rect r, string label, bool on, string tip = null,
                                    bool locked = false, bool warn = false)
        {
            bool over = !locked && Mouse.IsOver(r);
            if (over) Slab.Fill(r, Hover);
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);

            float size = Mathf.Min(Mathf.Round(LineH * 0.8f), r.height - 2f);
            var box = new Rect(r.x + 1f, r.y + (r.height - size) / 2f, size, size);

            // Checked is solid accent with no border of its own; unchecked is a hole in the
            // surface with one. Locked is the same pair at half opacity - Adwaita's
            // insensitive again, and the same call the buttons make.
            var face = on ? PrimeFace : Well;
            var edge = on ? Clear : BtnEdge;
            if (locked) { face = Fade(face, 0.5f); edge = Fade(edge, 0.5f); }
            Slab.Box(box, face, edge);
            if (on)
            {
                GUI.color = locked ? Faint : Color.white;
                GUI.DrawTexture(box.ContractedBy(2f), Icons.Check);
            }

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

            // Adwaita's knob is white on both the light and the dark theme, and squared off
            // it is the only solid white thing in the chrome - which is what makes a row of
            // sliders readable at a glance. Grabbed, it steps down rather than sinking.
            float knobX = Mathf.Lerp(track.x, track.xMax, Mathf.Clamp01(value));
            bool grabbed = GUIUtility.hotControl == id;
            Slab.Box(new Rect(knobX - knobW / 2f, track.y - 3f, knobW, track.height + 6f),
                grabbed ? Lighten(KnobFace, -0.20f) : Mouse.IsOver(hit)
                    ? Lighten(KnobFace, -0.08f) : KnobFace,
                Clear);
            return value;
        }

        // A page's ground. What `Widgets.DrawMenuSection` used to be, in the palette and
        // without the texture: Adwaita's card, one shade off the window with a line round it.
        //
        // Opaque, and that is the whole reason it exists. The pages are drawn inside the
        // options window, which is still vanilla's, so a card at any transparency is a page
        // with RimWorld's stone showing through the middle of it.
        public static void Card(Rect r) => Slab.Box(r, WindowBg, Edge);

        // An icon that answers to a press, in the chrome's own hover rather than vanilla's.
        // `Widgets.ButtonImage` tints the icon white on mouseover and draws nothing behind
        // it; Adwaita puts the button's white *under* the icon and leaves the icon alone,
        // which is what makes a row of them read as a row of buttons.
        //
        // `tint` is the icon's colour at rest - a disabled errand hands over a faded one -
        // and it goes to full white under the mouse.
        public static bool IconButton(Rect r, Texture2D icon, Color tint, bool on = true)
        {
            bool over = on && Mouse.IsOver(r);
            bool held = over && Input.GetMouseButton(0);

            if (over) Slab.Fill(r, held ? BtnDown : BtnHover);

            var was = GUI.color;
            GUI.color = on ? (over ? Lead : tint) : Fade(tint, 0.5f);
            GUI.DrawTexture(r.ContractedBy(2f), icon);
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

            const float Gap = 8f;
            const float Pad = 22f;   // room either side of the label inside the box

            public bool Left(string label, Btn kind = Btn.Default, bool on = true)
            {
                float w = Wide(label);
                var at = new Rect(_r.x + _left, _r.y, w, BtnH);
                _left += w + Gap;
                return Button(at, label, kind, on);
            }

            public bool Right(string label, Btn kind = Btn.Default, bool on = true)
            {
                float w = Wide(label);
                var at = new Rect(_r.xMax - _right - w, _r.y, w, BtnH);
                _right += w + Gap;
                return Button(at, label, kind, on);
            }

            public Rect Rest()
            {
                float x = _r.x + _left;
                return new Rect(x, _r.y, Mathf.Max(_r.xMax - _right - Gap - x, 0f), BtnH);
            }

            static float Wide(string label)
            {
                var was = Verse.Text.Font;
                Verse.Text.Font = GameFont.Small;
                float w = SlopWidgets.Wide(label);
                Verse.Text.Font = was;
                return Mathf.Max(w + Pad * 2f, 76f);
            }
        }

        public const string Unreachable =
            "Daemon unreachable. Is slopd running?  systemctl --user status slopd";

        public static void Fail(string msg) =>
            Messages.Message($"SlopWorld: {msg}", MessageTypeDefOf.RejectInput, false);

        const float MarkW = 30f;
        const float MarkSize = 18f;

        public static FloatMenuOption MenuToggle(string label, bool on, Action act)
        {
            var opt = new FloatMenuOption(label, act, MenuOptionPriority.Default, null, null,
                MarkW, r => DrawMark(r, on));
            opt.extraPartRightJustified = true;
            return opt;
        }

        static bool DrawMark(Rect r, bool on)
        {
            var icon = new Rect(r.x + (r.width - MarkSize) / 2f,
                r.y + (r.height - MarkSize) / 2f, MarkSize, MarkSize);
            GUI.color = on ? Yes : Dim;
            GUI.DrawTexture(icon, on ? Icons.Check : Icons.Cross);
            GUI.color = Color.white;
            // extraPartOnGUI's return means the extra part handled the click; the row does.
            return false;
        }

        public static void Header(Rect rect, string title, SessionHub hub)
        {
            Title(rect, title);
            Status(new Rect(rect.x, rect.y, rect.width, HeaderH), hub);
        }

        const float DotSize = 10f;
        const float PillPad = 10f;

        static void Status(Rect line, SessionHub hub)
        {
            var wasAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;

            string text = $"{SlopClient.BaseUrl} - {hub.Status}";
            float w = Wide(text);

            float h = RowH;
            var pill = new Rect(line.xMax - (w + DotSize + GapS + PillPad * 2f),
                line.y + (line.height - h) / 2f,
                w + DotSize + GapS + PillPad * 2f, h);
            Slab.Fill(pill, Well);

            var dot = new Rect(pill.x + PillPad, pill.y + (h - DotSize) / 2f,
                DotSize, DotSize);
            GUI.color = hub.Online ? Online : Offline;
            GUI.DrawTexture(dot, Icons.Dot);

            GUI.color = Dim;
            Widgets.Label(new Rect(dot.xMax + GapS, pill.y, w + 2f, h), text);
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
            Widgets.DrawBoxSolid(r, RowBg);
            if (Mouse.IsOver(r)) Slab.Fill(r, Hover);
        }

        public static bool HoverRow(Rect r)
        {
            bool on = ColonistBarStrip.Hover(r);
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
            var view = new Rect(0f, 0f, rect.width - 18f, items.Count * RowH + 4f);

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
                DrawRow(new Rect(0f, y, view.width, RowH - 4f), item);
                y += RowH;
            }

            _scroll.End();
        }
    }
}
