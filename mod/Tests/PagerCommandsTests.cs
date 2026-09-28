using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class PagerCommandsTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("quotes argv the way a shell would", QuotesArgv);
            yield return ("templates file and line into commands", TemplatesFileAndLine);
            yield return ("templates file actions with the requested path", FileActionPaths);
            yield return ("builds pager and editor invocations", BuildsPagerAndEditor);
            yield return ("recognizes configured pager and editor commands", RecognizesCommands);
            yield return ("displays original routed filenames", DisplayNames);
            yield return ("profile code themes preserve pager command boundaries", CodeThemes);
            yield return ("diff pager overrides replace safely", DiffPager);
            yield return ("line numbers belong to source views", LineNumbers);
            yield return ("code appearance stays draft until saved", AppearanceDraft);
            yield return ("code appearance save preserves subsequent edits", AppearancePendingSave);
            yield return ("failed appearance persistence preserves applied settings", AppearanceSaveFailure);
        }

        static void AppearanceDraft()
        {
            var settings = new ModSettings { codeBatTheme = "old", codeLineNumbers = true };
            var draft = CodeAppearanceDraft.For(settings);
            draft.SelectTheme("bat", "new");
            draft.LineNumbers = false;
            AssertEx.True(draft.Dirty, "theme and numbers are unsaved");
            AssertEx.Equal("new", draft.Theme("bat"), "preview uses draft theme");
            AssertEx.Equal("old", CodeHighlight.Theme(settings, "bat"), "readers retain applied theme");
            AssertEx.True(settings.codeLineNumbers, "readers retain applied numbers");
            AssertEx.True(ReferenceEquals(draft, CodeAppearanceDraft.For(settings)), "page reopening retains draft");
            draft.Discard();
            AssertEx.Equal("old", draft.Theme("bat"), "discard restores theme");
            AssertEx.True(draft.LineNumbers, "discard restores numbers");
            AssertEx.False(draft.Dirty, "discard is clean");
            draft.LineNumbers = false;
            int writes = 0;
            var save = draft.CaptureSave(value => { writes++; AssertEx.False(value.codeLineNumbers, "persist submitted numbers"); });
            AssertEx.Equal(0, writes, "capturing does not persist");
            AssertEx.True(settings.codeLineNumbers, "capturing does not apply");
            save();
            AssertEx.Equal(1, writes, "save persists once");
            AssertEx.False(settings.codeLineNumbers, "save applies numbers");
            AssertEx.False(draft.Dirty, "saved draft is clean");
        }

        static void AppearancePendingSave()
        {
            var settings = new ModSettings { codeBatTheme = "old" };
            var draft = CodeAppearanceDraft.For(settings);
            draft.SelectTheme("bat", "submitted");
            draft.LineNumbers = false;
            var save = draft.CaptureSave(_ => { });
            draft.SelectTheme("bat", "next");
            draft.LineNumbers = true;
            save();
            AssertEx.Equal("submitted", settings.codeBatTheme, "apply submitted theme");
            AssertEx.False(settings.codeLineNumbers, "apply submitted numbers");
            AssertEx.Equal("next", draft.Theme("bat"), "keep newer theme");
            AssertEx.True(draft.LineNumbers, "keep newer numbers");
            AssertEx.True(draft.Dirty, "newer edits remain unsaved");
        }

        static void AppearanceSaveFailure()
        {
            var settings = new ModSettings { codeBatTheme = "old", codeLineNumbers = true };
            var draft = CodeAppearanceDraft.For(settings);
            draft.SelectTheme("bat", "new");
            draft.LineNumbers = false;
            var save = draft.CaptureSave(_ => { throw new InvalidOperationException("write failed"); });
            bool failed = false;
            try { save(); }
            catch (InvalidOperationException) { failed = true; }
            AssertEx.True(failed, "persistence failure is reported");
            AssertEx.Equal("old", settings.codeBatTheme, "failed write restores applied theme");
            AssertEx.True(settings.codeLineNumbers, "failed write restores applied numbers");
            AssertEx.True(draft.Dirty, "failed draft remains available for retry");
        }

        static void LineNumbers()
        {
            AssertEx.Equal("less -N", PagerCommands.FilePager("less", true), "file numbers on");
            AssertEx.Equal("less -N -+N", PagerCommands.FilePager("less -N", false), "file numbers override custom flag");
            AssertEx.Equal("bat --paging=always --style=numbers --decorations=always --pager 'less -RS'", PagerCommands.FilePager("bat --paging=always", true), "bat file numbers on");
            AssertEx.Equal("bat --style=numbers --style=plain --decorations=never --pager 'less -RS' -- {file}",
                PagerCommands.FilePager("bat --style=numbers -- {file}", false), "bat numbers off before file argument");
            string pipe = PagerCommands.PipePager("bat --paging=always --style=numbers");
            AssertEx.True(pipe.EndsWith("--style=plain --decorations=never --color=never --language=txt --strip-ansi=never --pager 'less -RS'"),
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
            AssertEx.Equal("less -+N -S --", PagerCommands.PipePager("less -- {file}"),
                "pipe options precede trailing option terminator");
            AssertEx.Equal("bat --style=plain --decorations=never --color=never --language=txt --strip-ansi=never --pager 'less -RS' --",
                PagerCommands.PipePager("bat -- {file}"), "bat pipe options precede terminator");
            string first = PagerCommands.DiffCommand("git diff -- 'file name'", "less --prompt=it's");
            AssertEx.Equal("env DELTA_PAGER='less -N -+N -S' git -c delta.line-numbers=true diff -- 'file name'",
                PagerCommands.DiffCommand(first, "less -N"), "replace quoted pager without touching git arguments");
            string twice = PagerCommands.DiffCommand(PagerCommands.DiffCommand("git diff", "less"), "more");
            AssertEx.Equal("env DELTA_PAGER='more' git -c delta.line-numbers=true diff", twice, "only one override remains");
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
            AssertEx.Equal("less -+N -S", PagerCommands.PipePager("less {file}"),
                           "the pipe pager strips file/line placeholders");
            AssertEx.Equal("less -+N -S", PagerCommands.PipePager(""), "an empty pager falls back to less");

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
