using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Named palette for `SlopWidgets`; geometry stays fixed while selected colors propagate on
    // the next frame. Hex values are persisted in settings.
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
        // string this cannot read comes back as magenta, which is a color nobody chose and
        // therefore a color somebody notices.
        static Color Hex(string s) =>
            TerminalTheme.TryHex(s, out var c) ? c : Color.magenta;

        // Named palettes do not all publish a complete set of application-widget roles. This
        // maps their published background, foreground, accent and semantic colors onto the
        // rectangular UI's roles without inventing a second house palette for each one.
        static UIScheme Classic(string id, string label,
                                string background, string view, string popover,
                                string foreground, string muted, string accent,
                                string destructive, string warn, string yes, string global,
                                string stateWorking)
        {
            string alpha(string color, string a) => color + a;
            return new UIScheme(id, label,
                accent, destructive,
                background, view, popover, alpha(background, "f5"),
                alpha(destructive, "55"), "#0000008c",
                foreground, alpha(foreground, "cc"), muted, alpha(muted, "b0"),
                alpha(muted, "80"),
                destructive, warn, yes, global,
                alpha(muted, "40"), alpha(accent, "e6"),
                alpha(muted, "18"), alpha(muted, "70"),
                alpha(muted, "a0"), alpha(muted, "d0"),
                alpha(muted, "10"), alpha(muted, "30"), alpha(muted, "20"),
                stateWorking, warn, muted, destructive,
                alpha(muted, "30"), alpha(muted, "45"), alpha(muted, "70"),
                foreground);
        }

        public static readonly List<UIScheme> All = new List<UIScheme>
        {
            // The house scheme: an opaque grey panel, a signal blue, and every line, row and
            // face on it a wash of white at some strength. Restated here in hex, color for
            // color, from where these lived as floats in SlopWidgets.
            new UIScheme("slopworld", "SlopWorld",
                accent: "#3584e4", destructive: "#c01c28",
                windowBg: "#242424", viewBg: "#1e1e1e", popoverBg: "#2c2c2c",
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
                windowBg: "#282c34", viewBg: "#21252b", popoverBg: "#30343d",
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

            Classic("dracula", "Dracula",
                background: "#282a36", view: "#21222c", popover: "#343746",
                foreground: "#f8f8f2", muted: "#6272a4", accent: "#bd93f9",
                destructive: "#ff5555", warn: "#f1fa8c", yes: "#50fa7b",
                global: "#bd93f9", stateWorking: "#8be9fd"),

            Classic("gnome-dark", "GNOME Dark",
                background: "#241f31", view: "#1e1e1e", popover: "#3d3846",
                foreground: "#deddda", muted: "#9a9996", accent: "#62a0ea",
                destructive: "#e01b24", warn: "#e5a50a", yes: "#33d17a",
                global: "#c061cb", stateWorking: "#78aeed"),

            Classic("gnome-light", "GNOME Light",
                background: "#f6f5f4", view: "#ffffff", popover: "#ffffff",
                foreground: "#2e3436", muted: "#77767b", accent: "#3584e4",
                destructive: "#c01c28", warn: "#e5a50a", yes: "#26a269",
                global: "#9141ac", stateWorking: "#1c71d8"),

            Classic("tango-dark", "Tango Dark",
                background: "#2e3436", view: "#242729", popover: "#555753",
                foreground: "#eeeeec", muted: "#888a85", accent: "#729fcf",
                destructive: "#ef2929", warn: "#fce94f", yes: "#8ae234",
                global: "#ad7fa8", stateWorking: "#34e2e2"),

            Classic("tango-light", "Tango Light",
                background: "#eeeeec", view: "#ffffff", popover: "#f6f5f4",
                foreground: "#2e3436", muted: "#888a85", accent: "#3465a4",
                destructive: "#cc0000", warn: "#c4a000", yes: "#4e9a06",
                global: "#75507b", stateWorking: "#729fcf"),

            Classic("solarized-dark", "Solarized Dark",
                background: "#002b36", view: "#073642", popover: "#586e75",
                foreground: "#93a1a1", muted: "#657b83", accent: "#268bd2",
                destructive: "#dc322f", warn: "#b58900", yes: "#859900",
                global: "#6c71c4", stateWorking: "#2aa198"),

            Classic("solarized-light", "Solarized Light",
                background: "#fdf6e3", view: "#eee8d5", popover: "#fdf6e3",
                foreground: "#586e75", muted: "#839496", accent: "#268bd2",
                destructive: "#dc322f", warn: "#b58900", yes: "#859900",
                global: "#6c71c4", stateWorking: "#2aa198"),

            Classic("gruvbox", "Gruvbox Dark",
                background: "#282828", view: "#1d2021", popover: "#3c3836",
                foreground: "#ebdbb2", muted: "#928374", accent: "#83a598",
                destructive: "#fb4934", warn: "#fabd2f", yes: "#b8bb26",
                global: "#d3869b", stateWorking: "#8ec07c"),

            Classic("nord", "Nord",
                background: "#2e3440", view: "#3b4252", popover: "#434c5e",
                foreground: "#eceff4", muted: "#4c566a", accent: "#88c0d0",
                destructive: "#bf616a", warn: "#ebcb8b", yes: "#a3be8c",
                global: "#b48ead", stateWorking: "#81a1c1"),

            Classic("monokai", "Monokai",
                background: "#272822", view: "#1e1f1c", popover: "#414339",
                foreground: "#f8f8f2", muted: "#75715e", accent: "#a6e22e",
                destructive: "#f92672", warn: "#f4bf75", yes: "#a6e22e",
                global: "#ae81ff", stateWorking: "#66d9ef"),

            Classic("vscode-dark", "VS Code Dark+",
                background: "#1e1e1e", view: "#1e1e1e", popover: "#252526",
                foreground: "#d4d4d4", muted: "#858585", accent: "#007acc",
                destructive: "#f44747", warn: "#dcdcaa", yes: "#89d185",
                global: "#c586c0", stateWorking: "#4fc1ff"),
        };

        // A scheme this build no longer ships reads as the house one rather than as no
        // colors at all - the same bargain TerminalTheme.Get makes.
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
        // caches a chrome color into a texture, so there is no Rev here for anything to
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
