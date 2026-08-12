using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // The chrome's palette by name, the way TerminalTheme is the pane's - and for the same
    // reason. What a screen looks like is about the eyes reading it, and the window has two
    // halves that were never lit by the same taste: one is somebody else's terminal, the
    // other is ours.
    //
    // A scheme is a table of colours and nothing else. There is no geometry in it, no gaps
    // and no shapes: SlopWorld is a rectangular instrument panel under every scheme (see
    // mod-ui-identity), and what a scheme changes is the light it stands in. SlopWidgets
    // reads this and everything else reads SlopWidgets, so a scheme arrives everywhere the
    // frame after it is picked, with nothing to repaint and nothing to tell.
    //
    // Written as hex because a table of colours is read as a table. `#rrggbb`, or
    // `#rrggbbaa` for the washes - a structural line and a hovered row are the same colour
    // at two strengths, and the eighth digit keeps that pair on one line each.
    public class UIScheme
    {
        // Id is what the settings file holds; Label is what the options page says. They are
        // two things because "onedark" is a key and "One Dark" is a name.
        public readonly string Id, Label;

        public readonly Color Accent, Destructive;
        public readonly Color WindowBg, ViewBg, PopoverBg, Panel, OfflineBg, Scrim;
        public readonly Color Lead, Name, Dim, Faint, Off;
        public readonly Color Bad, Warn, Yes, Global;
        public readonly Color Edge, EdgeLit;
        public readonly Color ScrollTrough, ScrollThumb, ScrollThumbHover, ScrollThumbHeld;
        public readonly Color RowBg, RowOn, Hover;
        public readonly Color StateWorking, StateWaiting, StateIdle, StateDown;
        public readonly Color BtnFace, BtnHover, BtnDown, Knob;

        // Not in the table, because they are not choices: a well is the view's own surface
        // seen through a frame, and a selection is the accent at the one strength that lets
        // the row under it still be read.
        public readonly Color Well, Sel;

        // The scheme drawn rather than described, for the swatch strip on the options page.
        // Sixteen, like the pane's ANSI row, and for the same reason - it is the widest a
        // strip can be and still be one glance.
        public readonly Color[] Swatches;

        UIScheme(string id, string label,
                 string accent, string destructive,
                 string windowBg, string viewBg, string popoverBg, string panel,
                 string offlineBg, string scrim,
                 string lead, string name, string dim, string faint, string off,
                 string bad, string warn, string yes, string global,
                 string edge, string edgeLit,
                 string scrollTrough, string scrollThumb,
                 string scrollThumbHover, string scrollThumbHeld,
                 string rowBg, string rowOn, string hover,
                 string stateWorking, string stateWaiting, string stateIdle, string stateDown,
                 string btnFace, string btnHover, string btnDown, string knob)
        {
            Id = id;
            Label = label;

            Accent = Hex(accent);
            Destructive = Hex(destructive);

            WindowBg = Hex(windowBg);
            ViewBg = Hex(viewBg);
            PopoverBg = Hex(popoverBg);
            Panel = Hex(panel);
            OfflineBg = Hex(offlineBg);
            Scrim = Hex(scrim);

            Lead = Hex(lead);
            Name = Hex(name);
            Dim = Hex(dim);
            Faint = Hex(faint);
            Off = Hex(off);

            Bad = Hex(bad);
            Warn = Hex(warn);
            Yes = Hex(yes);
            Global = Hex(global);

            Edge = Hex(edge);
            EdgeLit = Hex(edgeLit);

            ScrollTrough = Hex(scrollTrough);
            ScrollThumb = Hex(scrollThumb);
            ScrollThumbHover = Hex(scrollThumbHover);
            ScrollThumbHeld = Hex(scrollThumbHeld);

            RowBg = Hex(rowBg);
            RowOn = Hex(rowOn);
            Hover = Hex(hover);

            StateWorking = Hex(stateWorking);
            StateWaiting = Hex(stateWaiting);
            StateIdle = Hex(stateIdle);
            StateDown = Hex(stateDown);

            BtnFace = Hex(btnFace);
            BtnHover = Hex(btnHover);
            BtnDown = Hex(btnDown);
            Knob = Hex(knob);

            Well = ViewBg;
            Sel = new Color(Accent.r, Accent.g, Accent.b, 0.35f);

            Swatches = new[]
            {
                Lead, Name, Dim, Faint, Off,
                Accent, Destructive,
                Yes, Warn, Bad, Global,
                StateWorking, StateWaiting, StateIdle, StateDown,
                EdgeLit,
            };
        }

        // The pane's parser, which is the only one there should be. Six digits or eight; a
        // string this cannot read comes back as magenta, which is a colour nobody chose and
        // therefore a colour somebody notices.
        static Color Hex(string s) =>
            TerminalTheme.TryHex(s, out var c) ? c : Color.magenta;

        public static readonly List<UIScheme> All = new List<UIScheme>
        {
            // The house scheme: an opaque grey panel, a signal blue, and every line, row and
            // face on it a wash of white at some strength. Restated here in hex, colour for
            // colour, from where these lived as floats in SlopWidgets.
            new UIScheme("slopworld", "SlopWorld",
                accent: "#3584e4", destructive: "#c01c28",
                windowBg: "#242424", viewBg: "#1e1e1e", popoverBg: "#383838",
                panel: "#242424f5", offlineBg: "#6b1f1aeb", scrim: "#0000008c",
                lead: "#ffffff", name: "#ffffffcc", dim: "#ffffff8c",
                faint: "#ffffff6b", off: "#ffffff4d",
                bad: "#ff7b63", warn: "#f8c635", yes: "#58c44b", global: "#c9b8e8",
                edge: "#ffffff24", edgeLit: "#8c99b3e6",
                scrollTrough: "#ffffff0a", scrollThumb: "#ffffff47",
                scrollThumbHover: "#ffffff6b", scrollThumbHeld: "#ffffff8c",
                rowBg: "#ffffff08", rowOn: "#ffffff1a", hover: "#ffffff0f",
                stateWorking: "#73bff2", stateWaiting: "#facc4d",
                stateIdle: "#999ea3", stateDown: "#d95959",
                btnFace: "#ffffff1a", btnHover: "#ffffff26", btnDown: "#ffffff4d",
                knob: "#ffffff"),

            // One Dark, the editor palette, kept honest: the surfaces are its own three
            // greys and the signals are its own syntax hues. The washes are laid in its
            // foreground rather than in white, because white over a blue-grey panel is a
            // colder line than that panel was drawn expecting.
            new UIScheme("onedark", "One Dark",
                accent: "#61afef", destructive: "#be5046",
                windowBg: "#282c34", viewBg: "#21252b", popoverBg: "#3a3f4b",
                panel: "#282c34f5", offlineBg: "#5f2a2feb", scrim: "#0000008c",
                lead: "#dcdfe4", name: "#abb2bf", dim: "#7f848e",
                faint: "#5c6370", off: "#4b5263",
                bad: "#e06c75", warn: "#e5c07b", yes: "#98c379", global: "#c678dd",
                edge: "#abb2bf2e", edgeLit: "#61afefe6",
                scrollTrough: "#abb2bf0f", scrollThumb: "#abb2bf47",
                scrollThumbHover: "#abb2bf6b", scrollThumbHeld: "#abb2bf8c",
                rowBg: "#abb2bf0a", rowOn: "#abb2bf1f", hover: "#abb2bf14",
                stateWorking: "#61afef", stateWaiting: "#e5c07b",
                stateIdle: "#7f848e", stateDown: "#e06c75",
                btnFace: "#abb2bf24", btnHover: "#abb2bf33", btnDown: "#abb2bf4d",
                knob: "#dcdfe4"),
        };

        // A scheme this build no longer ships reads as the house one rather than as no
        // colours at all - the same bargain TerminalTheme.Get makes.
        public static UIScheme Get(string id)
        {
            foreach (var s in All) if (s.Id == id) return s;
            return All[0];
        }

        static UIScheme _current;
        static string _id;

        // Resolved against the setting on every read rather than behind an Invalidate call:
        // this is a reference comparison against a string the settings object hands back
        // unchanged, and it is one branch on a path that is otherwise a field read. Nothing
        // caches a chrome colour into a texture, so there is no Rev here for anything to
        // watch - unlike the pane, whose row cache is keyed on the theme it was drawn in.
        public static UIScheme Current
        {
            get
            {
                string id = Settings.UIScheme;
                if (_current == null || _id != id)
                {
                    _current = Get(id);
                    _id = id;
                }
                return _current;
            }
        }
    }
}
