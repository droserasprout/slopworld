using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Tomlyn.Model;

namespace SlopWorld.Tests
{
    static class ThemeValidationTests
    {
        static string Shipped(bool terminal) => File.ReadAllText(Path.Combine("mod", "Themes",
            terminal ? "Terminal" : "UI", "slopworld-warm.toml"));

        static string Change(bool terminal, Action<TomlTable> edit)
        {
            var table = Toml.ParseTable(Shipped(terminal));
            edit(table);
            return global::Tomlyn.Toml.FromModel(table);
        }

        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            foreach (bool terminal in new[] { false, true })
            {
                var invalid = new (string Name, Action<TomlTable> Edit, string Error)[]
                {
                    ("missing version", t => t.Remove("version"), "requires version = 1"),
                    ("unsupported version", t => t["version"] = 2L, "requires version = 1"),
                    ("string version", t => t["version"] = "1", "requires version = 1"),
                    ("unknown key", t => t["typo"] = "value", "unknown key 'typo'"),
                    ("missing label", t => t.Remove("label"), "missing key 'label'"),
                    ("empty label", t => t["label"] = "", "missing string 'label'"),
                    ("numeric label", t => t["label"] = 12L, "missing string 'label'"),
                    ("negative order", t => t["order"] = -1L, "non-negative integer 'order'"),
                    ("overflowing order", t => t["order"] = (long)int.MaxValue + 1, "non-negative integer 'order'"),
                    ("string order", t => t["order"] = "0", "non-negative integer 'order'"),
                    ("unpinned built-in", t => t.Remove("order"), "may set order"),
                    ("pinned external theme", t => t["id"] = "custom", "may set order"),
                    ("noncontiguous order", t => t["order"] = 1L, "order must be contiguous from 0"),
                };
                foreach (var item in invalid)
                    yield return ($"{(terminal ? "terminal" : "UI")} theme rejects {item.Name}", () => Reject(terminal, item.Edit, item.Error));
                foreach (string color in new[] { "#123", "1234567", "#12zz34", "#123456789" })
                    yield return ($"{(terminal ? "terminal" : "UI")} theme rejects color {color}", () => Reject(terminal,
                        t => t[terminal ? "fg" : "accent"] = color, "not a valid color"));
                foreach (string kind in new[] { "missing", "empty", "duplicate", "malformed", "unmatched" })
                    yield return ($"{(terminal ? "terminal" : "UI")} theme directory rejects {kind}", () => BadDirectory(terminal, kind));
            }
            foreach (string role in new[] { "accent", "destructive", "checkFace" })
                yield return ($"UI theme requires opaque {role}", () => Reject(false,
                    t => t[role] = "#ABCDEF80", "requires opaque " + role));
            yield return ("UI theme requires a matching terminal ID", () => Reject(false,
                t => { t.Remove("order"); t["id"] = "unmatched"; }, "has no terminal theme for Match UI"));
            yield return ("terminal theme requires ANSI array", () => Reject(true, t => t["ansi"] = "colors", "missing array 'ansi'"));
            yield return ("terminal theme rejects numeric ANSI slot", () => Reject(true, t => ((TomlArray)t["ansi"])[4] = 42L, "ansi[4] is not a valid color"));
            yield return ("terminal theme rejects invalid ANSI color", () => Reject(true, t => ((TomlArray)t["ansi"])[9] = "#gggggg", "ansi[9] is not a valid color"));
        }

        static void Reject(bool terminal, Action<TomlTable> edit, string message)
        {
            string changed = Change(terminal, edit);
            var error = Assert.Throws<FormatException>(() => ThemeCatalog.LoadText(
                terminal ? Shipped(false) : changed, terminal ? changed : Shipped(true)));
            Assert.That(error.Message, Does.Contain(message));
        }

        static void BadDirectory(bool terminal, string kind)
        {
            string root = Path.Combine(Path.GetTempPath(), "slopworld-themes-" + Guid.NewGuid().ToString("N"));
            string ui = Path.Combine(root, "Themes", "UI"), term = Path.Combine(root, "Themes", "Terminal");
            Directory.CreateDirectory(ui);
            Directory.CreateDirectory(term);
            try
            {
                File.WriteAllText(Path.Combine(ui, "valid.toml"), Shipped(false));
                File.WriteAllText(Path.Combine(term, "valid.toml"), Shipped(true));
                string target = terminal ? term : ui;
                string source = Path.Combine(target, "valid.toml");
                string expected;
                switch (kind)
                {
                    case "unmatched":
                        File.WriteAllText(source, Change(terminal, t => { t.Remove("order"); t["id"] = "unmatched"; }));
                        expected = "has no terminal theme for Match UI";
                        break;
                    case "missing": Directory.Delete(target, true); expected = "theme directory is missing"; break;
                    case "empty": File.Delete(source); File.WriteAllText(Path.Combine(target, "ignored.txt"), "not a theme"); expected = "catalog has no files"; break;
                    case "malformed": File.WriteAllText(source, "not toml"); expected = "invalid " + source + " theme file"; break;
                    default:
                        string custom = Change(terminal, t => { t.Remove("order"); t["id"] = "duplicate"; });
                        File.WriteAllText(source, custom);
                        File.WriteAllText(Path.Combine(target, "copy.toml"), custom);
                        expected = "theme id is duplicated: duplicate";
                        break;
                }
                var error = Assert.Throws<FormatException>(() => ThemeCatalog.Load(root));
                Assert.That(error.Message, Does.Contain(expected));
            }
            finally { Directory.Delete(root, true); }
        }

        public static void AcceptsUppercaseAlphaColorsAndExternalThemesWithoutOrder()
        {
            var catalog = ThemeCatalog.LoadText(
                Change(false, t => { t.Remove("order"); t["id"] = "custom"; t["panel"] = "#ABCDEF80"; }),
                Change(true, t => { t.Remove("order"); t["id"] = "custom"; ((TomlArray)t["ansi"])[0] = "#ABCDEF"; }));
            Assert.That(catalog.UISchemes.Single().Order, Is.Null);
            Assert.That(catalog.UISchemes.Single().Panel, Is.EqualTo("#ABCDEF80"));
            Assert.That(catalog.TerminalThemes.Single().Order, Is.Null);
            Assert.That(catalog.TerminalThemes.Single().Ansi[0], Is.EqualTo("#ABCDEF"));
            Assert.Throws<FormatException>(() => ThemeCatalog.Load(null));
            Assert.Throws<FormatException>(() => ThemeCatalog.Load(""));
        }
    }
}
