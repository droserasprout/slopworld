using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    public class TerminalTheme
    {
        public readonly string Name;
        public readonly Color Fg, Bg;
        public readonly Color Cursor, CursorText;
        public readonly Color Selection;
        public readonly Color Link;
        public readonly Color[] Ansi;

        TerminalTheme(string name, string fg, string bg, string cursor, string cursorText,
                      string selection, string link, string[] ansi)
        {
            Name = name;
            Fg = Hex(fg);
            Bg = Hex(bg);
            Cursor = Hex(cursor);
            CursorText = Hex(cursorText);
            Selection = Hex(selection, 0.35f);
            Link = Hex(link);
            Ansi = new Color[16];
            for (int i = 0; i < 16; i++) Ansi[i] = Hex(ansi[i]);
        }

        public static Color Hex(string s, float a = 1f)
        {
            if (TryHex(s, out var c)) { c.a = a; return c; }
            return new Color(1f, 1f, 1f, a);
        }

        public static bool TryHex(string s, out Color c)
        {
            c = Color.white;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s.Length > 0 && s[0] == '#') s = s.Substring(1);
            if (s.Length != 6) return false;

            int v = 0;
            for (int i = 0; i < 6; i++)
            {
                int d = Digit(s[i]);
                if (d < 0) return false;
                v = v * 16 + d;
            }
            c = new Color(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
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
            new TerminalTheme("slopworld",
                fg: "#d3cbb8", bg: "#14120e",
                cursor: "#e0b64a", cursorText: "#14120e",
                selection: "#4a5a73", link: "#86a9c4",
                ansi: new[]
                {
                    "#1a1712", "#a9503c", "#7b8f4e", "#c39440",
                    "#5b7c99", "#8d6a93", "#6e9490", "#c2baa6",
                    "#4a453b", "#c86a50", "#9bb066", "#e0b64a",
                    "#7b9ebd", "#ae87b3", "#8cb6b1", "#efe7d4",
                }),

            new TerminalTheme("slate",
                fg: "#d4d9db", bg: "#0a0d0f",
                cursor: "#d4d9db", cursorText: "#0a0d0f",
                selection: "#4d80e6", link: "#7ab3ed",
                ansi: new[]
                {
                    "#242628", "#cc4a4f", "#73b55c", "#d9ad54",
                    "#5994d4", "#b073c7", "#59b8b8", "#c7cccf",
                    "#595e63", "#eb7073", "#94d478", "#f0cc73",
                    "#7ab3ed", "#cc94e3", "#78d6d6", "#f2f5f7",
                }),

            new TerminalTheme("gruvbox",
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

            new TerminalTheme("nord",
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

            new TerminalTheme("solarized",
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

            new TerminalTheme("paper",
                fg: "#33322e", bg: "#f6f2e8",
                cursor: "#b5762b", cursorText: "#f6f2e8",
                selection: "#8fa7c4", link: "#3a6a99",
                ansi: new[]
                {
                    "#2b2a26", "#a3402f", "#4f7a3a", "#a07a22",
                    "#3a6a99", "#8a5a96", "#3e8a85", "#d9d2c2",
                    "#6e6a5f", "#c25b45", "#6e9c52", "#c29a38",
                    "#5a8cc0", "#a87bb4", "#5caaa4", "#fffdf7",
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
