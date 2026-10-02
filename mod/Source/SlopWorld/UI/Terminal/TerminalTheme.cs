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
            Fg = HexColor.Hex(fg);
            Bg = HexColor.Hex(bg);
            Cursor = HexColor.Hex(cursor);
            CursorText = HexColor.Hex(cursorText);
            Selection = HexColor.Hex(selection, 0.35f);
            Link = HexColor.Hex(link);
            Ansi = new Color[16];
            for (int i = 0; i < Ansi.Length; i++) Ansi[i] = HexColor.Hex(ansi[i]);
        }

        TerminalTheme(ThemeCatalog.TerminalRecord record)
            : this(record.Id, record.Label, record.Fg, record.Bg, record.Cursor,
                   record.CursorText, record.Selection, record.Link, record.Ansi)
        { }

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
            _cursor = HexColor.TryHex(hex, out var c) ? c : _current.Cursor;
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
