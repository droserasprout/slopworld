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
            AssertEx.Equal("env LESS=-Rc less -- '/f'", PagerCommands.PagerCommand("less", "", "/f"),
                           "pager paints from the top and retains the alternate screen");
            AssertEx.Equal("LESSOPEN='|highlight %s' LESS=-Rc", PagerCommands.LessEnv("highlight"),
                           "a highlighter without %s gets one appended");
            AssertEx.Equal("less", PagerCommands.PipePager("less {file}"),
                           "the pipe pager strips file/line placeholders");
            AssertEx.Equal("less", PagerCommands.PipePager(""), "an empty pager falls back to less");

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
