using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    public static class SlopWidgets
    {

        public static readonly Color Lead = new Color(0.88f, 0.90f, 0.93f);

        public static readonly Color Name = new Color(0.76f, 0.78f, 0.82f);

        public static readonly Color Dim = new Color(0.62f, 0.64f, 0.68f);

        public static readonly Color Faint = new Color(0.50f, 0.52f, 0.57f);

        public static readonly Color Off = new Color(0.42f, 0.44f, 0.49f);

        public static readonly Color Bad = new Color(0.92f, 0.45f, 0.44f);

        public static readonly Color Warn = new Color(0.85f, 0.75f, 0.45f);

        public static readonly Color Global = new Color(0.79f, 0.72f, 0.91f);

        public static readonly Color Panel = new Color(0.09f, 0.10f, 0.12f, 0.93f);

        public static readonly Color Edge = new Color(0f, 0f, 0f, 0.55f);
        public static readonly Color EdgeLit = new Color(0.55f, 0.60f, 0.70f, 0.90f);

        public static readonly Color Well = new Color(0f, 0f, 0f, 0.25f);

        static readonly Color Online = new Color(0.5f, 0.8f, 0.5f);
        static readonly Color Offline = new Color(0.9f, 0.5f, 0.5f);

        public static readonly Color Yes = new Color(0.55f, 0.82f, 0.55f);

        public static readonly Color RowBg = new Color(1f, 1f, 1f, 0.03f);
        public static readonly Color RowOn = new Color(1f, 1f, 1f, 0.10f);

        public enum Btn
        {
            Default,   // the ordinary press: Reload, Browse, Edit.
            Primary,   // what the window was opened to do. One per bar, or it means nothing.
            Danger,    // takes something away. Still asks first; this is so it is read first.
            Ghost,     // there, but not competing - a press beside a press that matters more.
        }

        static readonly Color BtnEdge = new Color(0f, 0f, 0f, 0.42f);

        static readonly Color BtnFace = new Color(0.23f, 0.24f, 0.27f, 0.96f);

        static readonly Color GhostFace = new Color(1f, 1f, 1f, 0.055f);

        static readonly Color FocusEdge = new Color(0.34f, 0.51f, 0.74f, 0.85f);

        static readonly Color PrimeFace = new Color(0.15f, 0.33f, 0.57f, 0.98f);
        static readonly Color DangerFace = new Color(0.46f, 0.16f, 0.18f, 0.98f);

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

            Color face, text;
            switch (kind)
            {
                case Btn.Primary:
                    face = PrimeFace; text = Color.white; break;
                case Btn.Danger:
                    face = DangerFace; text = new Color(1f, 0.86f, 0.85f); break;
                case Btn.Ghost:
                    face = GhostFace; text = Name;
                    break;
                default:
                    face = BtnFace; text = Lead; break;
            }

            if (held) face = Lighten(face, -0.12f);
            else if (over) face = Lighten(face, kind == Btn.Ghost ? 0.06f : 0.10f);
            if (over && kind == Btn.Ghost) text = Lead;

            if (!on)
            {
                face = Lighten(face, -0.45f);
                text = Off;
            }

            Slab.Raised(r, face, BtnEdge, held || !on);

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

        static Color Lighten(Color c, float by)
        {
            var to = by >= 0f ? Color.white : Color.black;
            float t = Mathf.Abs(by);
            return new Color(Mathf.Lerp(c.r, to.r, t), Mathf.Lerp(c.g, to.g, t),
                Mathf.Lerp(c.b, to.b, t), c.a);
        }

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
            Slab.Box(r, Well, focused ? FocusEdge : BtnEdge);

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
            if (frame) Slab.Box(r, Well, focused ? FocusEdge : BtnEdge);

            var inner = frame ? r.ContractedBy(6f, 4f) : r;
            if (!on) return Stated(inner, text, TextAnchor.UpperLeft);

            GUI.SetNextControlName(name);
            return GUI.TextArea(inner, text ?? "", Bare(Verse.Text.CurTextAreaStyle));
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
            if (over) Widgets.DrawHighlight(r);
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);

            float size = Mathf.Min(Mathf.Round(LineH * 0.8f), r.height - 2f);
            var box = new Rect(r.x + 1f, r.y + (r.height - size) / 2f, size, size);
            var face = on ? PrimeFace : Well;
            if (locked) face = Lighten(face, -0.45f);
            Slab.Box(box, face, BtnEdge);
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
            Widgets.DrawHighlightIfMouseover(r);
        }

        public static bool HoverRow(Rect r)
        {
            bool on = ColonistBarStrip.Hover(r);
            if (on) Widgets.DrawHighlight(r);
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
        Vector2 _scroll;

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

            Widgets.BeginScrollView(rect, ref _scroll, view);

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

            Widgets.EndScrollView();
        }
    }
}
