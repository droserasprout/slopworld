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

        // Something that has not gone wrong and is going to matter later: a field saved now
        // that takes hold when slopd next restarts, a shortcut whose ground is thrown away
        // after it. Not `Bad` - red for a caveat is a page that cries wolf, and by the third
        // one nothing on it is read. Two files had this same amber written out.
        public static readonly Color Warn = new Color(0.85f, 0.75f, 0.45f);

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

        // The one inside a list row is tighter - the row is what has the padding - but not
        // tighter than the line it holds, or the border is drawn through the glyphs.
        public static float RowBtnH => Mathf.Max(LineH + 2f, 22f);

        // How wide a press has to be to hold what it says, with a floor so a short label is
        // still a button rather than a chip. `Bar` keeps its own pair of figures: a press in a
        // footer is a bigger thing than one at the end of a row.
        public static float BtnW(string label, float floor) =>
            Mathf.Max(Wide(label) + GapM, floor);

        // The spacing scale, and the whole of it. The gaps across these files ran 2, 4, 6, 8,
        // 10, 12, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 40 and 52 - eighteen figures for
        // four intentions, each arrived at by nudging until one screenshot looked right, and
        // each one wrong again the moment a font or a scale changes. Four now, named for what
        // the space is between rather than for how big it is.
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

        // One line of the small font, as tall as it actually draws, and the three things laid
        // out from it. Asked rather than written down: `Widgets.Label` ends in `GUI.Label`,
        // which clips glyphs to the rect it is given, and `Verse.Text` measures its line
        // heights off the font at startup - so a 22 here is 22 for the one font it was
        // eyeballed against and a cropped descender on every other. `AgentSidebar` has asked
        // this question since its two label lines lost their bottom pixel row to a figure.
        //
        // The padding on the other two is what a bordered box needs and a bare line does not:
        // a border and a line of glyphs cannot be the same pixels, or there is no face showing
        // above and below the text. It is why a press is 30 rather than 20, and a field is 30
        // for the same reason - so a field and a button on one row line up.
        public static float LineH => LineHOf(GameFont.Small);

        public static float FieldH => LineH + 8f;
        public static float RowH => LineH + 6f;

        // A window's title line, with room for the rule under it. Off the medium font for the
        // reason the three above are off the small one.
        public static float HeaderH => LineHOf(GameFont.Medium) + 8f;

        // One line of a tier, as tall as that tier actually draws - and asked of the tier that
        // will *draw* rather than of the one that was named. `Text.Font = GameFont.Tiny` is a
        // request and not an assignment: `Verse.Text` drops it back to Small whenever
        // `TinyFontSupported` is false, which is a language whose glyphs do not shrink, the
        // Steam Deck, an option in vanilla's own menu, and any frame with a long event on it.
        // A row laid out at Tiny's height and drawn in Small's is a row with its bottom rows of
        // pixels cut off - the same failure a figure written by hand makes, arrived at from the
        // other end.
        public static float LineHOf(GameFont font) =>
            Mathf.Ceil(Verse.Text.LineHeightOf(Real(font)));

        // Which tier a request for this one lands on.
        public static GameFont Real(GameFont font) =>
            font == GameFont.Tiny && !Verse.Text.TinyFontSupported ? GameFont.Small : font;

        // A line of the tier a second line is written in, and the row that holds one. The two
        // pixels are what the trees have always had round a tiny line; the height under them is
        // the font's, so a face with tall glyphs or a size dragged up takes the row with it.
        public static float TinyH => LineHOf(GameFont.Tiny);
        public static float TinyRowH => TinyH + 2f;

        // The width of one line of text, measured as one line. `Text.CalcSize` answers about a
        // *wrapped* block while `Text.WordWrap` is on, which for anything with a space in it is
        // the width of its longest word - so a pill sized from a sentence comes out the width of
        // the longest thing in it, and a button sized from "Edit as TOML" is sized for "TOML".
        public static float Wide(string text)
        {
            bool wrap = Verse.Text.WordWrap;
            Verse.Text.WordWrap = false;
            float w = Verse.Text.CalcSize(text ?? "").x;
            Verse.Text.WordWrap = wrap;
            return w;
        }

        // One line of text in a row, cut with an ellipsis rather than wrapped into it. Both
        // halves are load-bearing: `Truncate` measures through `CalcSize`, which under a wrap
        // hands back a width the string already fits, so nothing is cut - and then `Widgets.Label`
        // wraps it, and a two-line block centred in a one-line rect loses the top of one line and
        // the bottom of the other. Vanilla brackets its own row labels the same way, in every
        // list that truncates one.
        public static void RowLabel(Rect r, string text)
        {
            bool wrap = Verse.Text.WordWrap;
            Verse.Text.WordWrap = false;
            Widgets.Label(r, (text ?? "").Truncate(Mathf.Max(1f, r.width)));
            Verse.Text.WordWrap = wrap;
        }

        // A box to type in: the well vanilla gives it, and a line round it that lights while
        // it has the keyboard. Named so the caller can be told apart from its neighbours -
        // GUI.SetNextControlName wants one, and two boxes sharing a name share a focus.
        //
        // `on` false is the box that is there and is not yours to type in - a temporary
        // project's directory, the config editor with nothing loaded into it yet. Drawn as
        // the text rather than as a dead control: a field that took keystrokes and threw them
        // away on the next frame is what was there before, and it reads as a bug.
        public static string Field(Rect r, string name, string text, bool on = true)
        {
            bool focused = on && GUI.GetNameOfFocusedControl() == name;
            Slab.Box(r, Well, focused ? FocusEdge : BtnEdge);

            var inner = r.ContractedBy(6f, 0f);
            if (!on) return Stated(inner, text, TextAnchor.MiddleLeft);

            GUI.SetNextControlName(name);
            return GUI.TextField(inner, text ?? "", Bare(Verse.Text.CurTextFieldStyle));
        }

        // The same, several lines tall. `frame` off is the one inside a scroll view: the box
        // there belongs round the *view*, and one as tall as the content would have its border
        // somewhere off the bottom of the window. The caller draws it and the text goes in bare.
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

        // Vanilla's own text style with its box taken off: the well and the line round it are
        // Slab's here, and the style's would be a second border half a pixel inside the first.
        static GUIStyle Bare(GUIStyle of)
        {
            var style = new GUIStyle(of) { normal = { background = null } };
            style.focused.background = null;
            style.hover.background = null;
            return style;
        }

        // What a box shows when it is not one to type in. Answers the text it was handed, so
        // the caller assigns its field back to itself and nothing has to know which of the two
        // it drew.
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

        // A square and the tick already drawn for the float menus, rather than vanilla's
        // textured box - the one place the mark had to be redrawn to match everything else.
        // The whole row is the switch, the way a menu row is.
        //
        // `locked` is ticked and not yours to untick: a preset the command an agent runs asks
        // for anyway. Shown rather than hidden, that being the answer to "why is ~/.claude
        // bound", and the face comes down so the row reads as stated rather than as chosen.
        // `warn` is for a row that costs something to tick - a preset that hands the sandbox a
        // way back out. The label keeps the colour on hover too: a caveat that disappears when
        // the pointer arrives is one nobody reads at the moment they are deciding.
        public static bool Checkbox(Rect r, string label, bool on, string tip = null,
                                    bool locked = false, bool warn = false)
        {
            bool over = !locked && Mouse.IsOver(r);
            if (over) Widgets.DrawHighlight(r);
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);

            // Off the line rather than written down, so the tick stays the size of the word
            // beside it: at four fifths of a line it is 17 at the shipped font, which is the
            // figure this was. Floored into the row, a box taller than its row being a border
            // drawn through the rows above and below.
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

        // The same three, taking their row off a listing rather than a rect. Most of the forms
        // here are a `Listing_Standard`, whose own TextEntry and CheckboxLabeled draw vanilla's
        // chrome - so the row is taken from it and ours is drawn into it. The listing's column
        // and gap handling is what is being kept; the box it would have put there is not.
        public static string Field(Listing_Standard l, string name, string text, bool on = true) =>
            Field(l.GetRect(FieldH), name, text, on);

        public static bool Checkbox(Listing_Standard l, string label, bool on, string tip = null) =>
            Checkbox(l.GetRect(RowH), label, on, tip);

        public static void SectionHeading(Listing_Standard l, string text) =>
            SectionHeading(l.GetRect(RowH), text);

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
            // so a two-letter label is still a button rather than a chip. Measured as one
            // line, or a label with a space in it is sized for its longest word and then
            // wraps inside the box it was given - see SlopWidgets.Wide.
            static float Wide(string label)
            {
                var was = Verse.Text.Font;
                Verse.Text.Font = GameFont.Small;
                float w = SlopWidgets.Wide(label);
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
            GUI.DrawTexture(icon, on ? Icons.Check : Icons.Cross);
            GUI.color = Color.white;
            return false;
        }

        // The window's name at one end, the daemon it is a view of at the other, and a rule
        // under them both.
        //
        // The status used to start at the title's *measured* width plus a figure, and before
        // that at a figure per window - three of them, each nudged by hand until it cleared
        // one particular title. Laid out from the far end instead, the title's width stops
        // being anybody's business: the one thing the two ends can collide over is the status
        // being longer than the room, and that is a URL the player chose.
        public static void Header(Rect rect, string title, SessionHub hub)
        {
            Title(rect, title);
            Status(new Rect(rect.x, rect.y, rect.width, HeaderH), hub);
        }

        // The dot's size, and the room the pill leaves either side of what is in it.
        const float DotSize = 10f;
        const float PillPad = 10f;

        // A lamp and a line about the socket, in a well of their own at the right end. The
        // colour is on the dot rather than on the words: green text on a dark window reads as
        // something having gone right, which is not what an address and a status are.
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

        // A heading over a group of controls, in the rung a heading is - glanced at on the way
        // to what it names, not read. The rule takes the rest of the line, which is what makes
        // it a heading rather than one more line of body text in body colour: a form of those
        // reads as a wall, and every section in this mod was one.
        public static void SectionHeading(Rect r, string text)
        {
            var wasAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            var was = Text.Font;
            Text.Font = GameFont.Small;

            // As one line: a heading with a space in it measured under a wrap answers the width
            // of its longest word, and the rule would then start somewhere inside it.
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

        // The three pages of the options menu are one shape: a line saying what is being
        // edited, the body in a box under it, and a footer bar along the bottom. Here rather
        // than three times over, because each had written the same four figures - 28, 40, 34,
        // 32 - and three copies of one layout is three chances for one of them to be nudged
        // alone. The category row is the page's title, so the caption is all the top needs.
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

        // The row of presses along the bottom, wherever there is one: three pages and three
        // dialogs each wrote it as `yMax - 34f` with a height of 32, for a button that stands
        // 30 - two pixels of nothing under every footer in the mod.
        public static Rect FooterBar(Rect rect) =>
            new Rect(rect.x, rect.yMax - BtnH, rect.width, BtnH);

        // A dialog's own title: the window header without a daemon to state beside it. The
        // same height and the same rule, so a dialog and a list window do not disagree about
        // where the thing under the title begins.
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

        // The fill and the hover behind one row of a list.
        public static void RowChrome(Rect r)
        {
            Widgets.DrawBoxSolid(r, RowBg);
            Widgets.DrawHighlightIfMouseover(r);
        }

        // The hover behind one row of any of the column's lists, and whether the pointer is
        // on it. One call because each of the three trees had grown its own, and one of them
        // - the shortcuts view - was hit-testing in *screen* coordinates from inside its own
        // scroll view, where the group has already moved the mouse into view coordinates: it
        // lit the row the pointer was not on while its clicks (taken outside the group, off
        // the screen rects it stores) landed on the right one.
        //
        // The rect is always the one the row is *drawn* with, whichever space that is in.
        public static bool HoverRow(Rect r)
        {
            bool on = ColonistBarStrip.Hover(r);
            if (on) Widgets.DrawHighlight(r);
            return on;
        }

        // One entry per line, which is how every list of binds here is edited. A label over an
        // `Area`, so a column of binds is the same box as every other box on the page. The
        // floor is on the box rather than on the rect, so a squeezed window ends up with
        // boxes that overlap rather than boxes with nothing typeable in them.
        public static string PathList(Rect r, string name, string label, string text)
        {
            float h = RowH;
            SectionHeading(new Rect(r.x, r.y, r.width, h), label);
            var box = new Rect(r.x, r.y + h + GapXS, r.width,
                Mathf.Max(r.height - h - GapXS, 40f));
            return Area(box, name, text);
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

    // Agents, projects and shortcuts are one list drawn three times: a title with the
    // daemon beside it, a scrolling list of fixed-height rows, and a row of buttons along
    // the bottom. What differs is the rows and what the buttons do, so that is what a
    // subclass says and the rest is here.
    //
    // A content view rather than a window (see IContentView): all three were 720x480
    // floaters that opened over the chrome, which put them in the way of the column they
    // were about and left them the one part of this interface that did not follow the
    // shape of the rest. They fill the room a pane gets now.
    //
    // Generic in the row rather than indexed, because every one of these walks a list the
    // hub owns and wants it typed on the way through.
    public abstract class SlopListView<T> : IContentView
    {
        Vector2 _scroll;

        public abstract string Title { get; }

        // The pitch, not the height: a row is drawn 4px shorter so neighbours do not touch.
        protected abstract float RowH { get; }

        // What this view says when it has nothing to show and the daemon is *up*. The
        // other half of that answer is `SlopWidgets.Unreachable` and is nobody's to state.
        protected abstract string EmptyNote { get; }

        protected abstract IEnumerable<T> Rows { get; }

        protected abstract void DrawRow(Rect r, T item);

        protected abstract void DoFooter(Rect bar, SessionHub hub);

        // What the list needs asking for before it is drawn. Called on the way in rather
        // than in a constructor, a view being built by whatever opened it.
        public virtual void Opened() { }

        public virtual void Closed() { }

        public void Draw(Rect rect)
        {
            var hub = SessionHub.Instance;

            SlopWidgets.Header(rect, Title, hub);

            // The three figures this used to be laid out from - 40 for the header, 40 for the
            // footer, 32 up from the bottom for a 30-tall bar - are one height and one gap.
            float top = rect.y + SlopWidgets.HeaderH + SlopWidgets.GapS;
            float foot = SlopWidgets.BtnH + SlopWidgets.GapS;
            DrawList(new Rect(rect.x, top, rect.width, rect.yMax - foot - top), hub);

            DoFooter(new Rect(rect.x, rect.yMax - SlopWidgets.BtnH, rect.width,
                SlopWidgets.BtnH), hub);
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
