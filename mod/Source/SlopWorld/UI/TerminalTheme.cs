using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    public class TerminalTheme
    {
        public readonly string Name;
        public readonly string Label;
        public readonly Color Fg, Bg;
        public readonly Color Cursor, CursorText;
        public readonly Color Selection;
        public readonly Color Link;
        public readonly Color[] Ansi;

        TerminalTheme(string name, string label, string fg, string bg, string cursor, string cursorText,
                      string selection, string link, string[] ansi)
        {
            Name = name;
            Label = label;
            Fg = Hex(fg);
            Bg = Hex(bg);
            Cursor = Hex(cursor);
            CursorText = Hex(cursorText);
            Selection = Hex(selection, 0.35f);
            Link = Hex(link);
            Ansi = new Color[16];
            for (int i = 0; i < 16; i++) Ansi[i] = Hex(ansi[i]);
        }

        // The alpha multiplies rather than replaces, so a string that carried one keeps it.
        // Every color in this file is six digits, where the two are the same thing.
        public static Color Hex(string s, float a = 1f)
        {
            if (TryHex(s, out var c)) { c.a *= a; return c; }
            return new Color(1f, 1f, 1f, a);
        }

        // Six digits is a color. Eight is a color and the strength it is laid on at, which
        // is how the chrome's washes are written - see UIScheme.
        public static bool TryHex(string s, out Color c)
        {
            c = Color.white;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s.Length > 0 && s[0] == '#') s = s.Substring(1);
            if (s.Length != 6 && s.Length != 8) return false;

            uint v = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int d = Digit(s[i]);
                if (d < 0) return false;
                v = v * 16u + (uint)d;
            }
            // Six digits are eight with an opaque tail, so both read out the same way.
            if (s.Length == 6) v = (v << 8) | 0xFF;

            c = new Color(((v >> 24) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f,
                          ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
            return true;
        }

        static int Digit(char ch)
        {
            if (ch >= '0' && ch <= '9') return ch - '0';
            if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
            if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
            return -1;
        }

        public static readonly List<TerminalTheme> All = new List<TerminalTheme>
        {
            // House palettes come first. The other entries are named palettes with published
            // values, kept here as terminal-ready 16-color adaptations.
            new TerminalTheme("slopworld", "Warm",
                fg: "#d3cbb8", bg: "#14120e",
                cursor: "#e0b64a", cursorText: "#14120e",
                selection: "#3d4c64", link: "#86a9c4",
                ansi: new[]
                {
                    "#1a1712", "#b85b46", "#7b8f4e", "#c39440",
                    "#6889a6", "#99739e", "#6e9490", "#c2baa6",
                    "#5b5549", "#c86a50", "#9bb066", "#e0b64a",
                    "#7b9ebd", "#ae87b3", "#8cb6b1", "#efe7d4",
                }),

            new TerminalTheme("slopworld-cold-1", "Cold",
                fg: "#c4cbd4", bg: "#12171e",
                cursor: "#8bb8d6", cursorText: "#12171e",
                selection: "#394b60", link: "#8eb7dd",
                ansi: new[]
                {
                    "#171c23", "#b8646d", "#819d85", "#c1a875",
                    "#6689b7", "#927fa5", "#6f9fa3", "#c4cbd4",
                    "#5d6975", "#d17b83", "#a0bc9d", "#dec68a",
                    "#88add8", "#b093c1", "#91c5c3", "#e3e8ed",
                }),

            new TerminalTheme("slopworld-warm-2", "Warm-sat",
                fg: "#e0c39b", bg: "#1a110c",
                cursor: "#e5a84b", cursorText: "#1a110c",
                selection: "#5a3d2b", link: "#d29a6b",
                ansi: new[]
                {
                    "#20140e", "#c45a43", "#8c9a50", "#d39a44",
                    "#6887a1", "#ad7789", "#719b8e", "#d8c7a9",
                    "#634538", "#e2785d", "#abb66a", "#efc35c",
                    "#89a9bd", "#c193a5", "#9bc9ad", "#f2dfc1",
                }),

            new TerminalTheme("onedark", "One Dark",
                fg: "#abb2bf", bg: "#282c34",
                cursor: "#61afef", cursorText: "#282c34",
                selection: "#3e4451", link: "#61afef",
                ansi: new[]
                {
                    "#282c34", "#e06c75", "#98c379", "#e5c07b",
                    "#61afef", "#c678dd", "#56b6c2", "#abb2bf",
                    "#5c6370", "#e06c75", "#98c379", "#e5c07b",
                    "#61afef", "#c678dd", "#56b6c2", "#ffffff",
                }),

            new TerminalTheme("dracula", "Dracula",
                fg: "#f8f8f2", bg: "#282a36",
                cursor: "#f8f8f2", cursorText: "#282a36",
                selection: "#44475a", link: "#8be9fd",
                ansi: new[]
                {
                    "#21222c", "#ff5555", "#50fa7b", "#f1fa8c",
                    "#bd93f9", "#ff79c6", "#8be9fd", "#f8f8f2",
                    "#6272a4", "#ff6e6e", "#69ff94", "#ffffa5",
                    "#d6acff", "#ff92df", "#a4ffff", "#ffffff",
                }),

            new TerminalTheme("gnome-dark", "GNOME Dark",
                fg: "#deddda", bg: "#241f31",
                cursor: "#deddda", cursorText: "#241f31",
                selection: "#3d3846", link: "#62a0ea",
                ansi: new[]
                {
                    "#171421", "#c01c28", "#26a269", "#a2734c",
                    "#12488b", "#a347ba", "#2aa1b3", "#d0cfcc",
                    "#5e5c64", "#f66151", "#33d17a", "#e9ad0c",
                    "#2a7bde", "#c061cb", "#33c7de", "#ffffff",
                }),

            new TerminalTheme("gnome-light", "GNOME Light",
                fg: "#2e3436", bg: "#fafafa",
                cursor: "#2e3436", cursorText: "#fafafa",
                selection: "#d3d7cf", link: "#1c71d8",
                ansi: new[]
                {
                    "#171421", "#c01c28", "#26a269", "#a2734c",
                    "#12488b", "#a347ba", "#2aa1b3", "#d0cfcc",
                    "#5e5c64", "#f66151", "#33d17a", "#e9ad0c",
                    "#2a7bde", "#c061cb", "#33c7de", "#ffffff",
                }),

            new TerminalTheme("tango-dark", "Tango Dark",
                fg: "#d3d7cf", bg: "#2e3436",
                cursor: "#eeeeec", cursorText: "#2e3436",
                selection: "#555753", link: "#729fcf",
                ansi: new[]
                {
                    "#2e3436", "#cc0000", "#4e9a06", "#c4a000",
                    "#3465a4", "#75507b", "#06989a", "#d3d7cf",
                    "#555753", "#ef2929", "#8ae234", "#fce94f",
                    "#729fcf", "#ad7fa8", "#34e2e2", "#eeeeec",
                }),

            new TerminalTheme("tango-light", "Tango Light",
                fg: "#2e3436", bg: "#eeeeec",
                cursor: "#2e3436", cursorText: "#eeeeec",
                selection: "#d3d7cf", link: "#204a87",
                ansi: new[]
                {
                    "#2e3436", "#cc0000", "#4e9a06", "#c4a000",
                    "#3465a4", "#75507b", "#06989a", "#d3d7cf",
                    "#555753", "#ef2929", "#8ae234", "#fce94f",
                    "#729fcf", "#ad7fa8", "#34e2e2", "#eeeeec",
                }),

            new TerminalTheme("gruvbox", "Gruvbox Dark",
                fg: "#ebdbb2", bg: "#282828",
                cursor: "#fabd2f", cursorText: "#282828",
                selection: "#665c54", link: "#83a598",
                ansi: new[]
                {
                    "#282828", "#cc241d", "#98971a", "#d79921",
                    "#458588", "#b16286", "#689d6a", "#a89984",
                    "#928374", "#fb4934", "#b8bb26", "#fabd2f",
                    "#83a598", "#d3869b", "#8ec07c", "#ebdbb2",
                }),

            new TerminalTheme("nord", "Nord",
                fg: "#d8dee9", bg: "#2e3440",
                cursor: "#88c0d0", cursorText: "#2e3440",
                selection: "#4c566a", link: "#88c0d0",
                ansi: new[]
                {
                    "#3b4252", "#bf616a", "#a3be8c", "#ebcb8b",
                    "#81a1c1", "#b48ead", "#88c0d0", "#e5e9f0",
                    "#4c566a", "#bf616a", "#a3be8c", "#ebcb8b",
                    "#81a1c1", "#b48ead", "#8fbcbb", "#eceff4",
                }),

            new TerminalTheme("solarized", "Solarized Dark",
                fg: "#839496", bg: "#002b36",
                cursor: "#93a1a1", cursorText: "#002b36",
                selection: "#073642", link: "#268bd2",
                ansi: new[]
                {
                    "#073642", "#dc322f", "#859900", "#b58900",
                    "#268bd2", "#d33682", "#2aa198", "#eee8d5",
                    "#002b36", "#cb4b16", "#586e75", "#657b83",
                    "#839496", "#6c71c4", "#93a1a1", "#fdf6e3",
                }),

            new TerminalTheme("solarized-light", "Solarized Light",
                fg: "#586e75", bg: "#fdf6e3",
                cursor: "#586e75", cursorText: "#fdf6e3",
                selection: "#eee8d5", link: "#268bd2",
                ansi: new[]
                {
                    "#eee8d5", "#dc322f", "#859900", "#b58900",
                    "#268bd2", "#d33682", "#2aa198", "#073642",
                    "#fdf6e3", "#cb4b16", "#586e75", "#657b83",
                    "#839496", "#6c71c4", "#93a1a1", "#002b36",
                }),

            new TerminalTheme("monokai", "Monokai",
                fg: "#f8f8f2", bg: "#272822",
                cursor: "#f8f8f0", cursorText: "#272822",
                selection: "#49483e", link: "#66d9ef",
                ansi: new[]
                {
                    "#333333", "#c4265e", "#86b42b", "#b3b42b",
                    "#6a7ec8", "#8c6bc8", "#56adbc", "#e3e3dd",
                    "#666666", "#f92672", "#a6e22e", "#e2e22e",
                    "#819aff", "#ae81ff", "#66d9ef", "#f8f8f2",
                }),

            new TerminalTheme("vscode-dark", "VS Code Dark+",
                fg: "#d4d4d4", bg: "#1e1e1e",
                cursor: "#aeafad", cursorText: "#1e1e1e",
                selection: "#264f78", link: "#3794ff",
                ansi: new[]
                {
                    "#000000", "#cd3131", "#0dbc79", "#e5e510",
                    "#2472c8", "#bc3fbc", "#11a8cd", "#e5e5e5",
                    "#666666", "#cd3131", "#23d18b", "#f5f543",
                    "#3b8eea", "#d670d6", "#29b8db", "#e5e5e5",
                }),
        };

        public static TerminalTheme Get(string name)
        {
            foreach (var t in All) if (t.Name == name) return t;
            return All[0];
        }

        public static int Rev { get; private set; }

        static TerminalTheme _current;
        static string _name, _cursorHex;
        static Color _cursor;

        public static TerminalTheme Current
        {
            get
            {
                Resolve();
                return _current;
            }
        }

        public static Color CursorColor
        {
            get
            {
                Resolve();
                return _cursor;
            }
        }

        static void Resolve()
        {
            string name = Settings.Theme;
            string hex = Settings.CursorColor;
            if (_current != null && _name == name && _cursorHex == hex) return;

            _current = Get(name);
            _cursor = TryHex(hex, out var c) ? c : _current.Cursor;
            _name = name;
            _cursorHex = hex;
            Rev++;
        }

        public static void Invalidate()
        {
            _current = null;
            Rev++;
        }
    }
}
