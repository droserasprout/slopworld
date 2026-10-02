using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class PagerCommandsTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("quotes argv the way a shell would", QuotesArgv);
            yield return ("quoted executables and option boundaries", QuotedCommands);
            yield return ("templates file and line into commands", TemplatesFileAndLine);
            yield return ("templates file actions with the requested path", FileActionPaths);
            yield return ("builds pager and editor invocations", BuildsPagerAndEditor);
            yield return ("recognizes configured pager and editor commands", RecognizesCommands);
            yield return ("displays original routed filenames", DisplayNames);
            yield return ("profile code themes preserve pager command boundaries", CodeThemes);
            yield return ("diff pager overrides replace safely", DiffPager);
            yield return ("line numbers belong to source views", LineNumbers);
        }

        static void QuotedCommands()
        {
            string pager = "'/opt/my tools/less'";
            string command = PagerCommands.PagerCommand(pager, "highlight", "/a");
            AssertEx.True(PagerCommands.IsPagerCommand(pager, command), "quoted pager under env");
            AssertEx.False(PagerCommands.IsPagerCommand(pager, "env NOTE=" + pager + " cat /a"),
                "an executable inside an environment value is not a pager");
            AssertEx.True(PagerCommands.FilePager(pager, true).Contains("-N"), "quoted basename gets options");
            string editor = "\"/opt/my tools/micro\"";
            AssertEx.Equal(editor + " -clipboard terminal '/a' +7",
                PagerCommands.EditorCommand(editor, "/a", 7), "options follow complete executable");
            AssertEx.True(PagerCommands.IsEditorCommand(editor, PagerCommands.EditorCommand(editor, "/a")),
                "quoted editor classification");
            var settings = new ModSettings { codeHighlightTheme = "dark" };
            AssertEx.Equal("highlight --title 'a -- b %s'\t--style='dark' --\t%s",
                CodeHighlight.Command("highlight --title 'a -- b %s'\t--\t%s", settings),
                "quoted boundaries remain literal");
            AssertEx.Equal("highlight --style='dark' --",
                CodeHighlight.Command("highlight --", settings), "trailing terminator");
            AssertEx.Equal("highlight --style='dark' %s",
                CodeHighlight.Command("highlight %s", settings), "placeholder remains present");
        }

        static void LineNumbers()
        {
            AssertEx.Equal("less -N --shift=1 --wheel-lines=1", PagerCommands.FilePager("less", true), "file numbers on");
            AssertEx.Equal("less -N -+N --shift=1 --wheel-lines=1", PagerCommands.FilePager("less -N", false), "file numbers override custom flag");
            AssertEx.Equal("bat --paging=always --style=numbers --decorations=always --wrap=never --pager 'less -RS --shift=1 --wheel-lines=1'", PagerCommands.FilePager("bat --paging=always", true), "bat file numbers on");
            AssertEx.Equal("bat --style=numbers --style=plain --decorations=never --wrap=never --pager 'less -RS --shift=1 --wheel-lines=1' -- {file}",
                PagerCommands.FilePager("bat --style=numbers -- {file}", false), "bat numbers off before file argument");
            string pipe = PagerCommands.PipePager("bat --paging=always --style=numbers");
            AssertEx.True(pipe.EndsWith("--style=plain --decorations=never --color=never --language=txt --strip-ansi=never --wrap=never --pager 'less -RS --shift=1 --wheel-lines=1'", StringComparison.Ordinal),
                "bat passes through delta output without another gutter or highlighting pass");
            AssertEx.False(PagerCommands.FilePager("bat --pager 'less -R'", true).Contains("less -RS"),
                "explicit bat pager remains selected");
            string first = PagerCommands.DiffCommand("git diff HEAD", "bat --paging=always", true);
            string second = PagerCommands.DiffCommand(first, "bat --paging=always", false);
            AssertEx.True(second.Contains("git -c delta.line-numbers=false diff HEAD"), "delta source numbers disabled");
            AssertEx.False(second.Contains("delta.line-numbers=true"), "stale source numbering override removed");
            AssertEx.Equal("custom-pager", PagerCommands.FilePager("custom-pager", true), "unknown pager keeps its command");
        }

        static void DiffPager()
        {
            AssertEx.Equal("less -+N -S --shift=1 --wheel-lines=1 --", PagerCommands.PipePager("less -- {file}"),
                "pipe options precede trailing option terminator");
            AssertEx.Equal("bat --style=plain --decorations=never --color=never --language=txt --strip-ansi=never --wrap=never --pager 'less -RS --shift=1 --wheel-lines=1' --",
                PagerCommands.PipePager("bat -- {file}"), "bat pipe options precede terminator");
            string first = PagerCommands.DiffCommand("git diff -- 'file name'", "less --prompt=it's");
            AssertEx.Equal("env DELTA_PAGER='less -N -+N -S --shift=1 --wheel-lines=1' git -c delta.line-numbers=true diff -- 'file name'",
                PagerCommands.DiffCommand(first, "less -N"), "replace quoted pager without touching git arguments");
            string twice = PagerCommands.DiffCommand(PagerCommands.DiffCommand("git diff", "less"), "more");
            AssertEx.Equal("env DELTA_PAGER='more' git -c delta.line-numbers=true diff", twice, "only one override remains");
            string themed = PagerCommands.DiffCommand("git diff HEAD", "less", true, "Monokai Extended");
            AssertEx.True(themed.StartsWith("env BAT_THEME='Monokai Extended' DELTA_PAGER=", StringComparison.Ordinal),
                "bat theme reaches delta");
            AssertEx.True(themed.Contains("git -c delta.line-numbers=true -c delta.minus-style='syntax auto' diff HEAD"),
                "removed lines retain syntax colors");
            AssertEx.Equal(themed, PagerCommands.DiffCommand(themed, "less", true, "Monokai Extended"),
                "restarting a themed diff does not stack overrides");
            string unthemed = PagerCommands.DiffCommand(themed, "less");
            AssertEx.False(unthemed.Contains("BAT_THEME="), "switching highlighter removes the bat theme");
            AssertEx.False(unthemed.Contains("delta.minus-style="), "switching highlighter restores delta styling");
            string defaultTheme = PagerCommands.DiffCommand("git diff", "less", true, "");
            AssertEx.False(defaultTheme.Contains("BAT_THEME="), "command default does not pin a theme");
            AssertEx.True(defaultTheme.Contains("delta.minus-style='syntax auto'"),
                "bat's command default still colors removed lines");
        }

        static void CodeThemes()
        {
            var settings = new ModSettings { codeBatTheme = "Monokai Extended", codePygmentsTheme = "friendly" };
            AssertEx.Equal("bat --color=always --theme='Monokai Extended' -- %s",
                CodeHighlight.Command("bat --color=always -- %s", settings), "theme precedes filename");
            AssertEx.Equal("pygmentize -O style=monokai -P 'style=friendly'",
                CodeHighlight.Command("pygmentize -O style=monokai", settings), "profile overrides command theme");
            AssertEx.Equal("highlight --out-format=xterm256",
                CodeHighlight.Command("highlight --out-format=xterm256", settings), "empty theme inherits command");
            AssertEx.Equal("wrapper bat", CodeHighlight.Command("wrapper bat", settings), "custom wrapper stays intact");
            AssertEx.Equal("bat", CodeHighlight.Engine("'/opt/my tools/bat' --color=always"), "quoted executable");
            AssertEx.Equal("Monokai Extended", settings.codeBatTheme, "switching tools preserves other themes");
        }

        static void DisplayNames()
        {
            AssertEx.Equal("mod/Defs/PlayerPawn.xml", PagerCommands.RelativeFilePath("/repo", "/repo/mod/Defs/PlayerPawn.xml"), "preserve relative path");
            AssertEx.Equal("/repo-other/file", PagerCommands.RelativeFilePath("/repo", "/repo-other/file"), "respect root boundary");
            AssertEx.Equal("/storage/file", PagerCommands.RelativeFilePath(null, "/storage/file"), "storage path remains absolute");
        }

        static void QuotesArgv()
        {
            AssertEx.Equal("'a b'", PagerCommands.Quote("a b"), "spaces stay inside one argument");
            AssertEx.Equal("'it'\\''s'", PagerCommands.Quote("it's"), "an apostrophe is escaped");
            AssertEx.Equal("''", PagerCommands.Quote(null), "null becomes an empty argument");
        }

        static void TemplatesFileAndLine()
        {
            AssertEx.Equal("less +5 -- '/f'", PagerCommands.FileCommand("less", "less", "/f", 5),
                           "a placeholderless command gets +line then -- file");
            AssertEx.Equal("less -- '/f'", PagerCommands.FileCommand("", "less", "/f"),
                           "the fallback is used and no line is appended");
            AssertEx.Equal("code -g '/f':7", PagerCommands.FileCommand("code -g {file}:{line}", "less", "/f", 7),
                           "placeholders are filled instead of appended");
        }

        static void FileActionPaths()
        {
            AssertEx.Equal("rm -- '/repo/file name'",
                PagerCommands.FileActionCommand("rm -- {{ absolute_path }}", "/repo/file name",
                    "file name"),
                "the absolute placeholder keeps the host path");
            AssertEx.Equal("rm -- 'file name'",
                PagerCommands.FileActionCommand("rm -- {{ relative_path }}", "/repo/file name",
                    "file name"),
                "the relative placeholder keeps the project-relative path");
            AssertEx.Equal("rm '/repo/file name'",
                PagerCommands.FileActionCommand("rm", "/repo/file name", "file name"),
                "a placeholderless action appends the absolute path");
        }

        static void BuildsPagerAndEditor()
        {
            AssertEx.Equal("env LESS=-RSc less -- '/f'", PagerCommands.PagerCommand("less", "", "/f"),
                           "pager paints from the top and retains the alternate screen");
            AssertEx.Equal("LESSOPEN='|highlight %s' LESS=-RSc", PagerCommands.LessEnv("highlight"),
                           "a highlighter without %s gets one appended");
            AssertEx.Equal("less -+N -S --shift=1 --wheel-lines=1", PagerCommands.PipePager("less {file}"),
                           "the pipe pager strips file/line placeholders");
            AssertEx.Equal("less -+N -S --shift=1 --wheel-lines=1", PagerCommands.PipePager(""), "an empty pager falls back to less");

            AssertEx.Equal("micro -clipboard terminal -- '/f'", PagerCommands.EditorCommand("micro", "/f"),
                           "micro uses OSC 52 for the sidebar host clipboard");
            AssertEx.Equal("micro -clipboard terminal '/f' +5", PagerCommands.EditorCommand("micro", "/f", 5),
                           "the +line selector follows the file for micro");
            AssertEx.Equal("micro -clipboard internal -- '/f'",
                           PagerCommands.EditorCommand("micro -clipboard internal", "/f"),
                           "an explicit micro clipboard backend is respected");
            AssertEx.Equal("code -g '/f':5", PagerCommands.EditorCommand("code -g {file}:{line}", "/f", 5),
                           "an editor with placeholders keeps its own ordering");
        }

        static void RecognizesCommands()
        {
            AssertEx.True(PagerCommands.IsPagerCommand("less", "less -R -- 'f'"), "prefix match");
            AssertEx.True(PagerCommands.IsPagerCommand("less", "env LESS=-R less -- 'f'"),
                          "pager wrapped in env is recognized");
            AssertEx.False(PagerCommands.IsPagerCommand("less", "micro 'f'"), "another program is not the pager");

            AssertEx.True(PagerCommands.IsEditorCommand("micro", "micro +5 'f'"), "editor prefix match");
            AssertEx.True(PagerCommands.IsEditorCommand("", "micro"), "the fallback editor is recognized");
            AssertEx.False(PagerCommands.IsEditorCommand("micro", "less 'f'"), "the pager is not the editor");
        }
    }
}
