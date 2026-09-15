using System;
using System.IO;
using System.Linq;
using SlopWorld;

namespace SlopWorld.Tests
{
    static class ThemeCatalogTests
    {
        public static void LoadsShippedFilesInPickerOrder()
        {
            var catalog = ThemeCatalog.LoadDirectories(
                Path.Combine("mod", "Themes", "UI"),
                Path.Combine("mod", "Themes", "Terminal"));

            AssertEx.Equal(15, catalog.UISchemes.Count, "UI theme count");
            AssertEx.Equal(15, catalog.TerminalThemes.Count, "terminal theme count");
            AssertEx.Equal("slopworld-warm", catalog.UISchemes[0].Id, "first UI theme");
            AssertEx.Equal(0, catalog.UISchemes[0].Order.Value, "first UI pin");
            AssertEx.Equal("dracula", catalog.UISchemes[3].Id, "first alphabetical UI theme");
            AssertEx.True(!catalog.UISchemes[3].Order.HasValue,
                          "alphabetical UI theme is not pinned");
            AssertEx.Equal("onedark", catalog.UISchemes[9].Id, "alphabetical UI theme");
            AssertEx.Equal("vscode-dark", catalog.UISchemes.Last().Id, "last UI theme");
            AssertEx.Equal("solarized", catalog.TerminalThemes[10].Id, "solarized terminal id");
            AssertEx.Equal(16, catalog.TerminalThemes[0].Ansi.Length, "ANSI slot count");
            AssertEx.Equal("#2b2014f5", catalog.UISchemes[0].Panel, "UI alpha preserved");
            AssertEx.Equal("#3d4c64", catalog.TerminalThemes[0].Selection,
                           "terminal selection source preserved");
        }

        public static void RejectsMalformedAndPartialFiles()
        {
            string ui = "version = 1\norder = 0\nid = \"broken\"\nlabel = \"Broken\"\n";
            string terminal = "version = 1\norder = 0\nid = \"broken\"\nlabel = \"Broken\"\n";
            AssertEx.Throws<FormatException>(() => ThemeCatalog.LoadText(ui, terminal),
                                             "partial UI catalog");
            AssertEx.Throws<FormatException>(() => ThemeCatalog.LoadText("not toml", terminal),
                                             "malformed UI catalog");
        }

        public static void RejectsWrongAnsiLength()
        {
            string ui = File.ReadAllText(Path.Combine("mod", "Themes", "UI", "slopworld-warm.toml"));
            string terminal = File.ReadAllText(Path.Combine("mod", "Themes", "Terminal", "slopworld-warm.toml"))
                .Replace(", \"#efe7d4\"]", "]");
            var parsed = Toml.ParseTable(terminal);
            AssertEx.Equal(15, ((global::Tomlyn.Model.TomlArray)parsed["ansi"]).Count,
                           "valid TOML fixture has 15 ANSI colors");
            AssertEx.Throws<FormatException>(() => ThemeCatalog.LoadText(ui, terminal),
                                             "partial ANSI palette");
        }
    }
}
