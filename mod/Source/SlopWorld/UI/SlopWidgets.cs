using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    // The chrome more than one window draws the same way, and the colours that mean the
    // same thing wherever they are drawn. A helper is here rather than on a base class
    // whenever the thing that wants it is not a list window - `ConfigPage` is a category
    // of the options menu and `FilesView` is a panel, and both draw these.
    //
    // Not a theme: `TerminalTheme` is the pane's, and its colours answer to a terminal's
    // rules rather than to this mod's. Everything else draws from the ramp below - the
    // panels that used to keep palettes of their own had between them eleven greys for
    // five intentions, so the intent is the name and there is one grey per name.
    public static class SlopWidgets
    {
        // Four steps down from white, and nothing between them. Each panel used to name its
        // own greys and by four panels there were eleven, several a hundredth apart - two
        // files had written the same three figures twice and three more had landed within
        // noise of each other without meaning to. What a colour is *for* is the only thing
        // that ever distinguished them, so that is what is named, and a view wanting a
        // contrast picks two rungs rather than mixing a new grey.
        //
        // The lean is constant: every rung is a little bluer than it is warm, which is what
        // keeps the ramp reading as one ramp against the dark panel behind it.

        // The thing the row is about - a directory's name, the word that is the answer. Not
        // `Text`: that is Verse's, and it is what every font and measurement here goes
        // through.
        public static readonly Color Lead = new Color(0.88f, 0.90f, 0.93f);

        // A name in a list, beside brighter things that are not names.
        public static readonly Color Name = new Color(0.76f, 0.78f, 0.82f);

        // A second line about the thing on the first: a path under a name, a count beside
        // it, a note under a field. Also what a list with nothing in it says.
        public static readonly Color Dim = new Color(0.62f, 0.64f, 0.68f);

        // Glanced at rather than read: an age, a heading over a group, a placeholder.
        public static readonly Color Faint = new Color(0.50f, 0.52f, 0.57f);

        // Not the one you are on - an unselected tab, a view you are not looking at.
        public static readonly Color Off = new Color(0.42f, 0.44f, 0.49f);

        // Something that went wrong, said in text rather than in a message - the half of
        // an error that stays on screen.
        public static readonly Color Bad = new Color(0.92f, 0.45f, 0.44f);

        // The dark this mod's own chrome is drawn on, wherever it is drawn: the sidebar, the
        // top bar, and anything later that wants to sit on the map without belonging to it.
        public static readonly Color Panel = new Color(0.09f, 0.10f, 0.12f, 0.93f);

        // Where a panel stops. Lit is the same line while it is being dragged.
        public static readonly Color Edge = new Color(0f, 0f, 0f, 0.55f);
        public static readonly Color EdgeLit = new Color(0.55f, 0.60f, 0.70f, 0.90f);

        // Behind a text area, so an empty one still reads as a box to type in.
        public static readonly Color Well = new Color(0f, 0f, 0f, 0.25f);

        // The daemon behind all of this, up or down.
        static readonly Color Online = new Color(0.5f, 0.8f, 0.5f);
        static readonly Color Offline = new Color(0.9f, 0.5f, 0.5f);

        // Yes, on a row that states one about itself. There is no matching No: off is drawn
        // in `Dim`, because a red cross reads as something having gone wrong.
        public static readonly Color Yes = new Color(0.55f, 0.82f, 0.55f);

        // Behind a list row, under the hover; and behind the row you are on, over it.
        public static readonly Color RowBg = new Color(1f, 1f, 1f, 0.03f);
        public static readonly Color RowOn = new Color(1f, 1f, 1f, 0.10f);

        // What a press is *for*, which is the only thing a button here is asked. Vanilla's
        // ButtonText draws one slab for all four, so Delete and Save come out the same shape
        // in the same tan and the destructive one is told apart by reading it.
        public enum Btn
        {
            Default,   // the ordinary press: Reload, Browse, Edit.
            Primary,   // what the window was opened to do. One per bar, or it means nothing.
            Danger,    // takes something away. Still asks first; this is so it is read first.
            Ghost,     // there, but not competing - a press beside a press that matters more.
        }

        // The face, and the line round it. Adwaita's shape rather than its colours: the border
        // is **darker** than the face it encloses. A light outline reads as a thing lit from
        // behind, which is why the first pass glowed.
        //
        // The face is **opaque**, which is the half that was missing. A dark border round a
        // face at 0.065 alpha encloses nothing - the window shows through and what is left is
        // an outline, which is why the second pass read as a widget toolkit from twenty years
        // ago rather than as a button. Adwaita's border is subtle *because* there is a lit
        // surface inside it to be subtle against. So there is one here, and the border came
        // down as it stopped having to do the work alone.
        static readonly Color BtnEdge = new Color(0f, 0f, 0f, 0.42f);

        // A press is the same face a shade darker rather than a second colour, so a bar of
        // buttons stays one bar while one of them is held.
        static readonly Color BtnFace = new Color(0.23f, 0.24f, 0.27f, 0.96f);

        // The one face still drawn through: a ghost is quiet because the surface it names is
        // barely raised off the window, and an opaque grey at this contrast is not quiet, it
        // is a second button. The text carries the rest - `Name` rather than `Dim`, which is
        // what left Reload hard to pick out of the footer.
        static readonly Color GhostFace = new Color(1f, 1f, 1f, 0.055f);

        // Round a box that has the keyboard. Dimmer than `EdgeLit`, which is the sidebar's
        // grip under a drag and wants to be the brightest thing on screen for the second it
        // is held; a focus ring is worn for as long as somebody is typing.
        static readonly Color FocusEdge = new Color(0.34f, 0.51f, 0.74f, 0.85f);

        // Deeper than they look like they should be: these sit on a window whose own
        // background is dark, and a saturated fill at this size is a button that has to be
        // looked away from. Adwaita's suggested-action and destructive, taken down.
        static readonly Color PrimeFace = new Color(0.15f, 0.33f, 0.57f, 0.98f);
        static readonly Color DangerFace = new Color(0.46f, 0.16f, 0.18f, 0.98f);

        // The standing height of a press, and of the smaller one that sits inside a list row.
        // Written here because the bars and the rows that lay them out should agree without
        // each naming a number - and because 20 was too short for a bordered box: the border
        // and the text between them left no face showing above or below the glyphs.
        public const float BtnH = 30f;
        public const float RowBtnH = 22f;

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
                    // Quieter than the button beside it, not absent from it: the first pass
                    // drew nothing at all until the pointer was over it, and a control you
                    // have to sweep the window to find is not restrained, it is hidden.
                    face = GhostFace; text = Name;
                    break;
                default:
                    face = BtnFace; text = Lead; break;
            }

            // Toward white on hover and away from it on the press, both by a little: the
            // point is that the box answered, not that it changed.
            if (held) face = Lighten(face, -0.12f);
            else if (over) face = Lighten(face, kind == Btn.Ghost ? 0.06f : 0.10f);
            if (over && kind == Btn.Ghost) text = Lead;

            // Off: drawn flat and drained rather than hidden. A press that cannot be made yet
            // - Save with nothing loaded - is still one the page has, and a button that
            // vanished would read as a page with no save at all. Toward the face of the
            // window rather than transparent, or a Primary at low alpha is a blue ghost.
            if (!on)
            {
                face = Lighten(face, -0.45f);
                text = Off;
            }

            // Level with the surface while it is off, for the reason a press is: nothing that
            // cannot be pushed stands up off the page.
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

        // Toward white, or toward black for a negative amount. The alpha is left where it
        // was: lerping the whole colour would fade the one translucent face out as it lit up,
        // which is the opposite of what a hover is for.
        static Color Lighten(Color c, float by)
        {
            var to = by >= 0f ? Color.white : Color.black;
            float t = Mathf.Abs(by);
            return new Color(Mathf.Lerp(c.r, to.r, t), Mathf.Lerp(c.g, to.g, t),
                Mathf.Lerp(c.b, to.b, t), c.a);
        }

        // A box to type in: the well vanilla gives it, and a line round it that lights while
        // it has the keyboard. Named so the caller can be told apart from its neighbours -
        // GUI.SetNextControlName wants one, and two boxes sharing a name share a focus.
        public static string Field(Rect r, string name, string text)
        {
            bool focused = GUI.GetNameOfFocusedControl() == name;
            Slab.Box(r, Well, focused ? FocusEdge : BtnEdge);

            GUI.SetNextControlName(name);
            var style = new GUIStyle(Verse.Text.CurTextFieldStyle) { normal = { background = null } };
            style.focused.background = null;
            style.hover.background = null;
            return GUI.TextField(r.ContractedBy(6f, 0f), text ?? "", style);
        }

        // The same, several lines tall.
        public static string Area(Rect r, string name, string text)
        {
            bool focused = GUI.GetNameOfFocusedControl() == name;
            Slab.Box(r, Well, focused ? FocusEdge : BtnEdge);

            GUI.SetNextControlName(name);
            var style = new GUIStyle(Verse.Text.CurTextAreaStyle) { normal = { background = null } };
            style.focused.background = null;
            style.hover.background = null;
            return GUI.TextArea(r.ContractedBy(6f, 4f), text ?? "");
        }

        // A square and the tick already drawn for the float menus, rather than vanilla's
        // textured box - the one place the mark had to be redrawn to match everything else.
        // The whole row is the switch, the way a menu row is.
        public static bool Checkbox(Rect r, string label, bool on, string tip = null)
        {
            bool over = Mouse.IsOver(r);
            if (over) Widgets.DrawHighlight(r);
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);

            const float Size = 17f;
            var box = new Rect(r.x + 1f, r.y + (r.height - Size) / 2f, Size, Size);
            Slab.Box(box, on ? PrimeFace : Well, BtnEdge);
            if (on)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(box.ContractedBy(2f), MarkIcon.CheckTex);
            }

            var wasAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = over ? Lead : Name;
            Widgets.Label(new Rect(box.xMax + 8f, r.y, r.xMax - box.xMax - 8f, r.height), label);
            Text.Anchor = wasAnchor;
            GUI.color = Color.white;

            if (!Widgets.ButtonInvisible(r)) return on;

            SoundDefOf.Click.PlayOneShotOnCamera();
            return !on;
        }

        // One press per side, laid out from the end it belongs to rather than from a running
        // total of the widths before it. Every footer here used to carry the offsets of its
        // neighbours written down - `+138f`, `+276f` - which is a set of numbers to get wrong
        // every time a label changes length.
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

            // What is left between the two ends, for the one thing a footer has that is not a
            // press: the error a page puts beside its Save. Asked for after the buttons are
            // laid, so it is whatever room they left rather than a figure counted off the
            // longest label anybody might use.
            public Rect Rest()
            {
                float x = _r.x + _left;
                return new Rect(x, _r.y, Mathf.Max(_r.xMax - _right - Gap - x, 0f), BtnH);
            }

            // Measured rather than given: a button is as wide as what it says, with a floor
            // so a two-letter label is still a button rather than a chip.
            static float Wide(string label)
            {
                var was = Verse.Text.Font;
                Verse.Text.Font = GameFont.Small;
                float w = Verse.Text.CalcSize(label).x;
                Verse.Text.Font = was;
                return Mathf.Max(w + Pad * 2f, 76f);
            }
        }

        // The one answer to an empty list that is not about what the list holds, so no
        // window states its own.
        public const string Unreachable =
            "Daemon unreachable. Is slopd running?  systemctl --user status slopd";

        // Every refusal the player is shown takes this road, so the prefix is written once
        // and a message that skipped it would be the one that did not look like ours.
        public static void Fail(string msg) =>
            Messages.Message($"SlopWorld: {msg}", MessageTypeDefOf.RejectInput, false);

        // Up means close it; down means open it, and whatever the window needs asked for
        // first. The factory rather than an instance, so nothing is built for a window
        // that turns out to be a close.
        public static void ToggleWindow<T>(Func<T> make) where T : Window
        {
            var open = Find.WindowStack.WindowOfType<T>();
            if (open != null) { open.Close(); return; }

            Find.WindowStack.Add(make());
        }

        // Room at the right end of a menu row for the mark, and the mark inside it.
        const float MarkW = 30f;
        const float MarkSize = 18f;

        // A float-menu row that states one fact about itself: the label, and a tick or a
        // cross where the row ends. FloatMenuOption has no notion of a ticked row - the
        // nearest thing is `Disabled`, which greys one out and reads as broken rather than
        // as off - so the mark goes in the extra part vanilla already reserves at the right
        // edge, and the whole row stays the switch.
        public static FloatMenuOption MenuToggle(string label, bool on, Action act)
        {
            var opt = new FloatMenuOption(label, act, MenuOptionPriority.Default, null, null,
                MarkW, r => DrawMark(r, on));
            opt.extraPartRightJustified = true;
            return opt;
        }

        // Answers false always: the extra part's return is "this was clicked", and the row
        // under it has already taken the click.
        static bool DrawMark(Rect r, bool on)
        {
            var icon = new Rect(r.x + (r.width - MarkSize) / 2f,
                r.y + (r.height - MarkSize) / 2f, MarkSize, MarkSize);
            GUI.color = on ? Yes : Dim;
            GUI.DrawTexture(icon, on ? MarkIcon.CheckTex : MarkIcon.CrossTex);
            GUI.color = Color.white;
            return false;
        }

        // The window's name, and beside it the daemon this window is a view of. The status
        // line is laid out from the title's measured width rather than from a figure per
        // window: three of those had been nudged by hand to clear three different titles,
        // which is a thing to get wrong every time a title changes.
        public static void Header(Rect rect, string title, SessionHub hub)
        {
            Text.Font = GameFont.Medium;
            float w = Text.CalcSize(title).x;
            Widgets.Label(new Rect(rect.x, rect.y, 300f, 32f), title);
            Text.Font = GameFont.Small;

            GUI.color = hub.Online ? Online : Offline;
            Widgets.Label(new Rect(rect.x + w + 16f, rect.y + 8f, 400f, 24f),
                $"{SlopClient.BaseUrl} - {hub.Status}");
            GUI.color = Color.white;
        }

        // The fill and the hover behind one row of a list.
        public static void RowChrome(Rect r)
        {
            Widgets.DrawBoxSolid(r, RowBg);
            Widgets.DrawHighlightIfMouseover(r);
        }

        // One entry per line, which is how every list of binds here is edited. The floor is
        // on the box rather than on the rect, so a squeezed window ends up with boxes that
        // overlap rather than boxes with nothing typeable in them.
        public static string PathList(Rect r, string label, string text)
        {
            Widgets.Label(new Rect(r.x, r.y, r.width, 22f), label);
            var box = new Rect(r.x, r.y + 22f, r.width, Mathf.Max(r.height - 22f, 40f));
            Widgets.DrawBoxSolid(box, Well);
            return Widgets.TextArea(box.ContractedBy(4f), text);
        }

        // "claude" -> "claude-2", and a copy of that -> "claude-3" rather than "claude-2-2".
        // Suggested and not enforced - the daemon still refuses a collision, which is why
        // the search gives up rather than looping. `taken` is asked for rather than looked
        // up, agents and projects being two tables with one rule about names.
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

    // Agents, projects and shortcuts are one window drawn three times: a title with the
    // daemon beside it, a scrolling list of fixed-height rows, and a row of buttons along
    // the bottom. What differs is the rows and what the buttons do, so that is what a
    // subclass says and the rest is here.
    //
    // Generic in the row rather than indexed, because every one of these walks a list the
    // hub owns and wants it typed on the way through.
    public abstract class SlopListWindow<T> : Window
    {
        Vector2 _scroll;

        protected SlopListWindow()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            preventCameraMotion = false;
            closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize => new Vector2(720f, 480f);

        protected abstract string Title { get; }

        // The pitch, not the height: a row is drawn 4px shorter so neighbours do not touch.
        protected abstract float RowH { get; }

        // What this window says when it has nothing to show and the daemon is *up*. The
        // other half of that answer is `SlopWidgets.Unreachable` and is nobody's to state.
        protected abstract string EmptyNote { get; }

        protected abstract IEnumerable<T> Rows { get; }

        protected abstract void DrawRow(Rect r, T item);

        protected abstract void DoFooter(Rect bar, SessionHub hub);

        public override void DoWindowContents(Rect rect)
        {
            var hub = SessionHub.Instance;

            SlopWidgets.Header(rect, Title, hub);

            float top = rect.y + 40f;
            DrawList(new Rect(rect.x, top, rect.width, rect.height - top - 40f), hub);

            DoFooter(new Rect(rect.x, rect.yMax - 32f, rect.width, 30f), hub);
        }

        void DrawList(Rect rect, SessionHub hub)
        {
            // Snapshotted, and once: the hub is pumped from a `Root.Update` postfix, so the
            // list a frame is sized from has to be the list that frame draws.
            var items = Rows.ToList();
            var view = new Rect(0f, 0f, rect.width - 18f, items.Count * RowH + 4f);

            Widgets.BeginScrollView(rect, ref _scroll, view);

            if (items.Count == 0)
            {
                GUI.color = SlopWidgets.Dim;
                Widgets.Label(new Rect(4f, 8f, view.width - 8f, 64f),
                    hub.Online ? EmptyNote : SlopWidgets.Unreachable);
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
