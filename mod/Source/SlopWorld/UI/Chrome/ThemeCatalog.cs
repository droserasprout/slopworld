using System;
using System.Collections.Generic;
using System.IO;
using TomlynArray = global::Tomlyn.Model.TomlArray;
using TomlynTable = global::Tomlyn.Model.TomlTable;

namespace SlopWorld
{
    // Runtime data boundary for shipped themes. Settings deliberately remain a flat TOML
    // schema; this reader owns the structured per-file catalog schema and returns typed data.
    public sealed class ThemeCatalog
    {
        public sealed class UiRecord
        {
            public readonly int? Order;
            public readonly string Id, Label;
            public readonly string Accent;
            public readonly string Destructive;
            public readonly string AccentText;
            public readonly string DestructiveText;
            public readonly string CheckFace;
            public readonly string WindowBg;
            public readonly string ViewBg;
            public readonly string PopoverBg;
            public readonly string Panel;
            public readonly string OfflineBg;
            public readonly string Scrim;
            public readonly string Lead;
            public readonly string Name;
            public readonly string Dim;
            public readonly string Faint;
            public readonly string Off;
            public readonly string Bad;
            public readonly string Warn;
            public readonly string Yes;
            public readonly string Global;
            public readonly string Edge;
            public readonly string EdgeLit;
            public readonly string ScrollTrough;
            public readonly string ScrollThumb;
            public readonly string ScrollThumbHover;
            public readonly string ScrollThumbHeld;
            public readonly string RowBg;
            public readonly string RowOn;
            public readonly string Hover;
            public readonly string StateWorking;
            public readonly string StateWaiting;
            public readonly string StateIdle;
            public readonly string StateDown;
            public readonly string BtnFace;
            public readonly string BtnHover;
            public readonly string BtnDown;
            public readonly string Knob;

            public UiRecord(int? order, string id, string label,
                            string accent,
                            string destructive,
                            string accentText,
                            string destructiveText,
                            string checkFace,
                            string windowBg,
                            string viewBg,
                            string popoverBg,
                            string panel,
                            string offlineBg,
                            string scrim,
                            string lead,
                            string name,
                            string dim,
                            string faint,
                            string off,
                            string bad,
                            string warn,
                            string yes,
                            string global,
                            string edge,
                            string edgeLit,
                            string scrollTrough,
                            string scrollThumb,
                            string scrollThumbHover,
                            string scrollThumbHeld,
                            string rowBg,
                            string rowOn,
                            string hover,
                            string stateWorking,
                            string stateWaiting,
                            string stateIdle,
                            string stateDown,
                            string btnFace,
                            string btnHover,
                            string btnDown,
                            string knob)
            {
                Order = order;
                Id = id;
                Label = label;
                Accent = accent;
                Destructive = destructive;
                AccentText = accentText;
                DestructiveText = destructiveText;
                CheckFace = checkFace;
                WindowBg = windowBg;
                ViewBg = viewBg;
                PopoverBg = popoverBg;
                Panel = panel;
                OfflineBg = offlineBg;
                Scrim = scrim;
                Lead = lead;
                Name = name;
                Dim = dim;
                Faint = faint;
                Off = off;
                Bad = bad;
                Warn = warn;
                Yes = yes;
                Global = global;
                Edge = edge;
                EdgeLit = edgeLit;
                ScrollTrough = scrollTrough;
                ScrollThumb = scrollThumb;
                ScrollThumbHover = scrollThumbHover;
                ScrollThumbHeld = scrollThumbHeld;
                RowBg = rowBg;
                RowOn = rowOn;
                Hover = hover;
                StateWorking = stateWorking;
                StateWaiting = stateWaiting;
                StateIdle = stateIdle;
                StateDown = stateDown;
                BtnFace = btnFace;
                BtnHover = btnHover;
                BtnDown = btnDown;
                Knob = knob;
            }
        }

        public sealed class TerminalRecord
        {
            public readonly int? Order;
            public readonly string Id, Label;
            public readonly string Fg, Bg, Cursor, CursorText, Selection, Link;
            public readonly string[] Ansi;

            public TerminalRecord(int? order, string id, string label,
                                  string fg, string bg, string cursor,
                                  string cursorText, string selection, string link,
                                  string[] ansi)
            {
                Order = order;
                Id = id;
                Label = label;
                Fg = fg;
                Bg = bg;
                Cursor = cursor;
                CursorText = cursorText;
                Selection = selection;
                Link = link;
                Ansi = (string[])ansi.Clone();
            }
        }
        public readonly List<UiRecord> UISchemes;
        public readonly List<TerminalRecord> TerminalThemes;

        ThemeCatalog(List<UiRecord> uiSchemes, List<TerminalRecord> terminalThemes)
        {
            UISchemes = uiSchemes;
            TerminalThemes = terminalThemes;
        }

        public static ThemeCatalog Load(string modRoot)
        {
            if (string.IsNullOrEmpty(modRoot))
                throw new FormatException("theme catalog has no mod root");
            return LoadDirectories(Path.Combine(modRoot, "Themes", "UI"),
                                   Path.Combine(modRoot, "Themes", "Terminal"));
        }

        public static ThemeCatalog LoadDirectories(string uiDirectory, string terminalDirectory)
        {
            return new ThemeCatalog(ReadUiDirectory(uiDirectory), ReadTerminalDirectory(terminalDirectory));
        }

        public static ThemeCatalog LoadText(string uiText, string terminalText)
        {
            var ui = ReadUiFile(ParseText(uiText, "UI"), "UI text");
            var terminal = ReadTerminalFile(ParseText(terminalText, "terminal"), "terminal text");
            return new ThemeCatalog(OrderUi(new List<UiRecord> { ui }),
                                    OrderTerminal(new List<TerminalRecord> { terminal }));
        }

        static List<UiRecord> ReadUiDirectory(string directory)
        {
            var records = new List<UiRecord>();
            foreach (string path in Files(directory, "UI"))
                records.Add(ReadUiFile(ReadFile(path, "UI"), path));
            return OrderUi(records);
        }

        static List<TerminalRecord> ReadTerminalDirectory(string directory)
        {
            var records = new List<TerminalRecord>();
            foreach (string path in Files(directory, "terminal"))
                records.Add(ReadTerminalFile(ReadFile(path, "terminal"), path));
            return OrderTerminal(records);
        }

        static string[] Files(string directory, string kind)
        {
            try
            {
                if (!Directory.Exists(directory))
                    throw new FormatException(kind + " theme directory is missing: " + directory);
                var paths = Directory.GetFiles(directory, "*.toml");
                Array.Sort(paths, StringComparer.Ordinal);
                return paths;
            }
            catch (FormatException) { throw; }
            catch (Exception e)
            {
                throw new FormatException("could not enumerate " + kind +
                                           " theme directory " + directory, e);
            }
        }

        static TomlynTable ReadFile(string path, string kind)
        {
            try { return ParseText(File.ReadAllText(path), path); }
            catch (FormatException) { throw; }
            catch (Exception e)
            {
                throw new FormatException(kind + " theme file could not be read at " + path, e);
            }
        }

        static TomlynTable ParseText(string text, string source)
        {
            try { return Toml.ParseTable(text); }
            catch (Exception e)
            {
                throw new FormatException("invalid " + source + " theme file: " + e.Message, e);
            }
        }

        static UiRecord ReadUiFile(TomlynTable table, string path)
        {
            RequireVersion(table, path);
            RequireKeys(table, UiKeys, path, OptionalKeys);
            return new UiRecord(
                OptionalInt(table, "order", path),
                Required(table, "id", path, false),
                Required(table, "label", path, false),
                Required(table, "accent", path, true),
                Required(table, "destructive", path, true),
                Required(table, "accentText", path, true),
                Required(table, "destructiveText", path, true),
                Required(table, "checkFace", path, true),
                Required(table, "windowBg", path, true),
                Required(table, "viewBg", path, true),
                Required(table, "popoverBg", path, true),
                Required(table, "panel", path, true),
                Required(table, "offlineBg", path, true),
                Required(table, "scrim", path, true),
                Required(table, "lead", path, true),
                Required(table, "name", path, true),
                Required(table, "dim", path, true),
                Required(table, "faint", path, true),
                Required(table, "off", path, true),
                Required(table, "bad", path, true),
                Required(table, "warn", path, true),
                Required(table, "yes", path, true),
                Required(table, "global", path, true),
                Required(table, "edge", path, true),
                Required(table, "edgeLit", path, true),
                Required(table, "scrollTrough", path, true),
                Required(table, "scrollThumb", path, true),
                Required(table, "scrollThumbHover", path, true),
                Required(table, "scrollThumbHeld", path, true),
                Required(table, "rowBg", path, true),
                Required(table, "rowOn", path, true),
                Required(table, "hover", path, true),
                Required(table, "stateWorking", path, true),
                Required(table, "stateWaiting", path, true),
                Required(table, "stateIdle", path, true),
                Required(table, "stateDown", path, true),
                Required(table, "btnFace", path, true),
                Required(table, "btnHover", path, true),
                Required(table, "btnDown", path, true),
                Required(table, "knob", path, true));
        }

        static TerminalRecord ReadTerminalFile(TomlynTable table, string path)
        {
            RequireVersion(table, path);
            RequireKeys(table, TerminalKeys, path, OptionalKeys);
            var ansi = RequiredArray(table, "ansi", path);
            if (ansi.Count != 16)
                throw new FormatException(path + " must have exactly 16 ANSI colors");
            var colors = new string[16];
            for (int slot = 0; slot < colors.Length; slot++)
            {
                object value = ansi[slot];
                if (!(value is string) || !IsHex((string)value))
                    throw new FormatException(path + " ansi[" + slot + "] is not a valid color");
                colors[slot] = (string)value;
            }
            return new TerminalRecord(
                OptionalInt(table, "order", path),
                Required(table, "id", path, false),
                Required(table, "label", path, false),
                Required(table, "fg", path, true),
                Required(table, "bg", path, true),
                Required(table, "cursor", path, true),
                Required(table, "cursorText", path, true),
                Required(table, "selection", path, true),
                Required(table, "link", path, true), colors);
        }

        static List<UiRecord> OrderUi(List<UiRecord> records)
        {
            records.Sort((a, b) =>
            {
                if (a.Order.HasValue != b.Order.HasValue) return a.Order.HasValue ? -1 : 1;
                if (a.Order.HasValue) return a.Order.Value.CompareTo(b.Order.Value);
                return string.CompareOrdinal(a.Id, b.Id);
            });
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int pinned = 0;
            for (int i = 0; i < records.Count; i++)
            {
                bool isPinned = records[i].Id.StartsWith("slopworld-", StringComparison.Ordinal);
                if (records[i].Order.HasValue != isPinned)
                    throw new FormatException("only SlopWorld UI themes may set order");
                if (isPinned && records[i].Order.Value != pinned++)
                    throw new FormatException("SlopWorld UI theme order must be contiguous from 0");
                if (!ids.Add(records[i].Id))
                    throw new FormatException("UI theme id is duplicated: " + records[i].Id);
            }
            if (records.Count == 0) throw new FormatException("UI theme catalog has no files");
            return records;
        }

        static List<TerminalRecord> OrderTerminal(List<TerminalRecord> records)
        {
            records.Sort((a, b) =>
            {
                if (a.Order.HasValue != b.Order.HasValue) return a.Order.HasValue ? -1 : 1;
                if (a.Order.HasValue) return a.Order.Value.CompareTo(b.Order.Value);
                return string.CompareOrdinal(a.Id, b.Id);
            });
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int pinned = 0;
            for (int i = 0; i < records.Count; i++)
            {
                bool isPinned = records[i].Id.StartsWith("slopworld-", StringComparison.Ordinal);
                if (records[i].Order.HasValue != isPinned)
                    throw new FormatException("only SlopWorld terminal themes may set order");
                if (isPinned && records[i].Order.Value != pinned++)
                    throw new FormatException("SlopWorld terminal theme order must be contiguous from 0");
                if (!ids.Add(records[i].Id))
                    throw new FormatException("terminal theme id is duplicated: " + records[i].Id);
            }
            if (records.Count == 0) throw new FormatException("terminal theme catalog has no files");
            return records;
        }

        static void RequireVersion(TomlynTable table, string path)
        {
            object value;
            if (!table.TryGetValue("version", out value) || !(value is long) || (long)value != 1)
                throw new FormatException(path + " requires version = 1");
        }

        static int? OptionalInt(TomlynTable table, string key, string path)
        {
            object value;
            if (!table.TryGetValue(key, out value)) return null;
            if (!(value is long) || (long)value < 0 || (long)value > int.MaxValue)
                throw new FormatException(path + " requires non-negative integer '" + key + "'");
            return (int)(long)value;
        }

        static string Required(TomlynTable table, string key, string path, bool color)
        {
            object value;
            if (!table.TryGetValue(key, out value) || !(value is string) ||
                string.IsNullOrEmpty((string)value))
                throw new FormatException(path + " is missing string '" + key + "'");
            string text = (string)value;
            if (color && !IsHex(text))
                throw new FormatException(path + " '" + key + "' is not a valid color");
            return text;
        }

        static TomlynArray RequiredArray(TomlynTable table, string key, string path)
        {
            object value;
            if (!table.TryGetValue(key, out value) || !(value is TomlynArray))
                throw new FormatException(path + " is missing array '" + key + "'");
            return (TomlynArray)value;
        }

        static void RequireKeys(TomlynTable table, string[] required, string path,
                                string[] optional)
        {
            var keys = new HashSet<string>(required, StringComparer.Ordinal);
            foreach (var key in optional) keys.Add(key);
            foreach (var pair in table)
                if (!keys.Contains(pair.Key))
                    throw new FormatException(path + " has unknown key '" + pair.Key + "'");
            foreach (var key in required)
                if (!table.ContainsKey(key))
                    throw new FormatException(path + " is missing key '" + key + "'");
        }

        static bool IsHex(string value)
        {
            if (value == null || value.Length != 7 && value.Length != 9 || value[0] != '#')
                return false;
            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F'))
                    return false;
            }
            return true;
        }

        static readonly string[] UiKeys =
        {
            "version", "id", "label", "accent", "destructive", "accentText",
            "destructiveText", "checkFace", "windowBg", "viewBg", "popoverBg", "panel",
            "offlineBg", "scrim", "lead", "name", "dim", "faint", "off", "bad", "warn",
            "yes", "global", "edge", "edgeLit", "scrollTrough", "scrollThumb",
            "scrollThumbHover", "scrollThumbHeld", "rowBg", "rowOn", "hover", "stateWorking",
            "stateWaiting", "stateIdle", "stateDown", "btnFace", "btnHover", "btnDown", "knob",
        };

        static readonly string[] TerminalKeys =
        {
            "version", "id", "label", "fg", "bg", "cursor", "cursorText",
            "selection", "link", "ansi",
        };

        static readonly string[] OptionalKeys = { "order" };
    }
}
