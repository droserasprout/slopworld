using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

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
            for (int i = 0; i < Ansi.Length; i++) Ansi[i] = Hex(ansi[i]);
        }

        TerminalTheme(ThemeCatalog.TerminalRecord record)
            : this(record.Id, record.Label, record.Fg, record.Bg, record.Cursor,
                   record.CursorText, record.Selection, record.Link, record.Ansi)
        { }

        public static Color Hex(string s, float a = 1f)
        {
            if (TryHex(s, out var c)) { c.a *= a; return c; }
            return new Color(1f, 1f, 1f, a);
        }

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

        static List<TerminalTheme> _all;

        public static List<TerminalTheme> All
        {
            get
            {
                if (_all == null) _all = LoadAll();
                return _all;
            }
        }

        static List<TerminalTheme> LoadAll()
        {
            try
            {
                string root = ModEntry.Instance?.Content?.RootDir;
                var catalog = ThemeCatalog.Load(root);
                var themes = new List<TerminalTheme>(catalog.TerminalThemes.Count);
                foreach (var record in catalog.TerminalThemes) themes.Add(new TerminalTheme(record));
                return themes;
            }
            catch (Exception e)
            {
                Log.Error("[SlopWorld] could not load terminal theme catalog: " + e);
                return new List<TerminalTheme> { Fallback() };
            }
        }

        static TerminalTheme Fallback() => new TerminalTheme("slopworld-warm", "SlopWorld Warm",
            fg: "#d3cbb8", bg: "#14120e", cursor: "#e0b64a", cursorText: "#14120e",
            selection: "#3d4c64", link: "#86a9c4",
            ansi: new[]
            {
                "#1a1712", "#b85b46", "#7b8f4e", "#c39440",
                "#6889a6", "#99739e", "#6e9490", "#c2baa6",
                "#5b5549", "#c86a50", "#9bb066", "#e0b64a",
                "#7b9ebd", "#ae87b3", "#8cb6b1", "#efe7d4",
            });

        public const string MatchUI = "match-ui";

        static int _rev;
        public static int Rev { get { Resolve(); return _rev; } }

        static TerminalTheme _current;
        static string _name, _cursorHex;
        static Color _cursor;

        public static TerminalTheme Current
        {
            get { Resolve(); return _current; }
        }

        public static Color CursorColor
        {
            get { Resolve(); return _cursor; }
        }

        static void Resolve()
        {
            string name = Settings.Theme;
            if (name == MatchUI)
                name = UIScheme.Current.Id;
            string hex = Settings.CursorColor;
            if (_current != null && _name == name && _cursorHex == hex) return;
            _current = Get(name);
            _cursor = TryHex(hex, out var c) ? c : _current.Cursor;
            _name = name;
            _cursorHex = hex;
            _rev++;
        }

        public static TerminalTheme Get(string name)
        {
            foreach (var theme in All) if (theme.Name == name) return theme;
            return All[0];
        }

        public static void Invalidate()
        {
            _current = null;
            _rev++;
        }
    }
}
