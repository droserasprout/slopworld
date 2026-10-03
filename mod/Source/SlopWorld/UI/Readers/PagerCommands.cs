using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Text;

namespace SlopWorld
{
    public static class PagerCommands
    {
        const string LessScrollOptions = "--shift=1 --wheel-lines=1";
        public static string RelativeFilePath(string root, string path)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return path;
            string prefix = root.TrimEnd('/') + "/";
            return path.StartsWith(prefix, StringComparison.Ordinal) ? path.Substring(prefix.Length) : path;
        }

        // The daemon splits a command line into an argv the way a shell would. Therefore, a path
        // with a space in it is two arguments unless it says otherwise. Both views build command
        // lines out of paths they were handed, so the quoting lives here.
        public static string Quote(string s) => "'" + (s ?? "").Replace("'", "'\\''") + "'";

        // File actions support both the path as it appears on the host and the path relative
        // to the project root. Without either placeholder, preserve the historical behavior
        // of appending the absolute path as the final argument.
        public static string FileActionCommand(string template, string path, string relativePath)
        {
            string command = (template ?? "").Trim();
            string absolute = Quote(path);
            string relative = Quote(relativePath ?? ".");
            bool substituted = false;
            if (command.Contains("{{ absolute_path }}"))
            {
                command = command.Replace("{{ absolute_path }}", absolute);
                substituted = true;
            }
            if (command.Contains("{{ relative_path }}"))
            {
                command = command.Replace("{{ relative_path }}", relative);
                substituted = true;
            }
            return substituted ? command : command + " " + absolute;
        }

        static string App(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        internal readonly struct CommandWord
        {
            public readonly string Value;
            public readonly int Start, End;
            public readonly bool Quoted;
            public CommandWord(string value, int start, int end, bool quoted)
            { Value = value; Start = start; End = end; Quoted = quoted; }
        }

        // Decode shell words without expanding them; retain source offsets for safe insertion.
        internal static List<CommandWord> Words(string command)
        {
            var words = new List<CommandWord>();
            command = command ?? "";
            int at = 0;
            while (at < command.Length)
            {
                while (at < command.Length && char.IsWhiteSpace(command[at])) at++;
                if (at == command.Length) break;
                int start = at;
                char quote = '\0';
                bool quoted = false;
                var value = new StringBuilder();
                while (at < command.Length)
                {
                    char c = command[at];
                    if (quote == '\0' && char.IsWhiteSpace(c)) break;
                    at++;
                    if (c == '\\' && quote != '\'' && at < command.Length)
                    {
                        char next = command[at];
                        if (quote == '\0' || next == '"' || next == '\\' || next == '$' || next == '`' || next == '\n')
                        {
                            quoted = true;
                            at++;
                            if (next != '\n') value.Append(next);
                            continue;
                        }
                    }
                    if (c == quote) { quote = '\0'; quoted = true; continue; }
                    if (quote == '\0' && (c == '\'' || c == '"'))
                    { quote = c; quoted = true; continue; }
                    value.Append(c);
                }
                words.Add(new CommandWord(value.ToString(), start, at, quoted));
            }
            return words;
        }

        internal static string Executable(string value, string fallback = "")
        {
            var words = Words(App(value, fallback));
            return words.Count == 0 ? "" : words[0].Value;
        }

        internal static string InsertOptions(string command, string options, string placeholder)
        {
            var words = Words(command);
            for (int i = 1; i < words.Count; i++)
            {
                var word = words[i];
                if (!word.Quoted && (word.Value == "--" || word.Value == placeholder))
                    return command.Insert(word.Start, options + " ");
            }
            return command + " " + options;
        }

        // Sidebar editors run through slopd, so micro's external clipboard backend cannot
        // reach the host display from a project sandbox. Its terminal backend emits OSC 52,
        // which slopd already delivers to the host clipboard. Respect an explicit clipboard
        // choice in a configured micro command.
        static string MicroTerminalClipboard(string command)
        {
            string executable = Executable(command, "micro");
            if (!string.Equals(Path.GetFileName(executable), "micro", StringComparison.Ordinal) ||
                command.IndexOf("-clipboard", StringComparison.Ordinal) >= 0)
                return command;

            int end = Words(command)[0].End;
            return command.Insert(end, " -clipboard terminal");
        }

        // The env vars for a persistent `less` that pipes every file through `highlight`.
        // `%s` is less's own placeholder for the filename, expanded on every `:e`.
        public static string LessEnv(string highlighter)
        {
            string h = (highlighter ?? "").Trim();
            // Paint from the top instead of scrolling a short first screen into place.
            // less and tmux can disagree about widths inside joined emoji. Avoid
            // wrapping a long line at different cells in the two terminal grids.
            if (h.Length == 0) return "LESS=-RSc";
            if (!h.Contains("%s")) h += " %s";
            return "LESSOPEN=" + Quote("|" + h) + " LESS=-RSc";
        }

        // Git feeds its diff to the pager on stdin, so it needs the configured command
        // without file/line placeholders or the normal file argument.
        public static string PipePager(string pager)
        {
            string command = App(pager, "less").Replace("{file}", "").Replace("{line}", "").Trim();
            switch (Path.GetFileName(Executable(command, "less")))
            {
                case "bat":
                    return BatOptions(command, "--style=plain --decorations=never --color=never --language=txt --strip-ansi=never --wrap=never");
                case "less": return Options(command, "-+N -S " + LessScrollOptions);
                default: return command;
            }
        }

        // File numbering belongs to the pager; diff numbering belongs to delta.
        public static string FilePager(string pager, bool lineNumbers)
        {
            string command = App(pager, "less");
            switch (Path.GetFileName(Executable(command, "less")))
            {
                case "bat": return BatOptions(command, (lineNumbers ? "--style=numbers --decorations=always" : "--style=plain --decorations=never") + " --wrap=never");
                case "less": return Options(command, (lineNumbers ? "-N" : "-+N") + " " + LessScrollOptions);
                default: return command;
            }
        }

        static string Options(string command, string options) =>
            InsertOptions(command, options, "{file}");

        static string BatOptions(string command, string options)
        {
            command = Options(command, options);
            // bat can wrap before it starts less. Disable both wrap stages, since
            // less and tmux can disagree about the width of joined emoji.
            if (!Regex.IsMatch(command, @"(?:^|\s)--pager(?:=|\s)"))
                command = Options(command, "--pager " + Quote("less -RS " + LessScrollOptions));
            return command;
        }

        static readonly Regex DeltaPagerPrefix = new Regex(@"^env (?:BAT_THEME='(?:[^']|'\\'')*' )?DELTA_PAGER='(?:[^']|'\\'')*' ");
        static readonly Regex DeltaNumbersPrefix = new Regex(@"^git -c delta\.line-numbers=(?:true|false) ");
        static readonly Regex DeltaBatStylePrefix = new Regex(@"^git -c delta\.minus-style='syntax auto' ");

        // Replace our launch overrides when recovering or restarting a diff session.
        // A non-null batTheme means bat is the selected highlighter; an empty value
        // keeps bat's and delta's respective command defaults.
        public static string DiffCommand(string command, string pager, bool lineNumbers = true, string batTheme = null)
        {
            string git = DeltaPagerPrefix.Replace(command ?? "", "", 1);
            git = DeltaNumbersPrefix.Replace(git, "git ", 1);
            git = DeltaBatStylePrefix.Replace(git, "git ", 1);
            if (git.StartsWith("git ", StringComparison.Ordinal))
            {
                if (batTheme != null)
                    git = "git -c delta.minus-style=" + Quote("syntax auto") + git.Substring(3);
                git = "git -c delta.line-numbers=" + (lineNumbers ? "true" : "false") + git.Substring(3);
            }
            string theme = string.IsNullOrEmpty(batTheme) ? "" : "BAT_THEME=" + Quote(batTheme) + " ";
            return "env " + theme + "DELTA_PAGER=" + Quote(PipePager(pager)) + " " + git;
        }

        public static bool IsPagerCommand(string pager, string command) =>
            CommandExecutable(command, allowEnv: true) == Executable(pager, "less");

        public static bool IsEditorCommand(string editor, string command) =>
            CommandExecutable(command, allowEnv: false) == Executable(editor, "micro");

        static string CommandExecutable(string command, bool allowEnv)
        {
            var words = Words(command);
            if (words.Count == 0) return null;
            int at = 0;
            if (allowEnv && words[0].Value == "env")
            {
                at++;
                while (at < words.Count && words[at].Value.IndexOf('=') > 0) at++;
            }
            return at < words.Count ? words[at].Value : null;
        }

        public static string FileCommand(string value, string fallback, string file, long line = 0)
        {
            string template = App(value, fallback);
            bool hasFile = template.Contains("{file}");
            bool hasLine = template.Contains("{line}");
            string command = template
                .Replace("{file}", Quote(file))
                .Replace("{line}", (line < 1 ? 1 : line).ToString(CultureInfo.InvariantCulture));
            if (!hasFile)
            {
                if (line > 0 && !hasLine) command += " +" + (line < 1 ? 1 : line);
                command += " -- " + Quote(file);
            }
            return command;
        }

        public static string PagerCommand(string pager, string highlighter, string file, long line = 0)
        {
            string environment = LessEnv(highlighter);
            // bat opens the file and starts less itself. Its argv cannot contain less's
            // +LINE selector; pass that selector to the child pager through LESS instead.
            if (line > 0 && Path.GetFileName(Executable(pager, "less")) == "bat" &&
                !(pager ?? "").Contains("{line}"))
            {
                environment = environment.Replace("LESS=-RSc", "LESS=" + Quote("-RSc +" + line.ToString(CultureInfo.InvariantCulture)));
                line = 0;
            }
            return "env " + environment + " " + FileCommand(pager, "less", file, line);
        }

        // micro's +LINE selector follows the file. FileCommand puts a pager's selector before
        // its `-- FILE`, which makes +LINE look like a buffer name to micro.
        public static string EditorCommand(string editor, string file, long line = 0)
        {
            string template = MicroTerminalClipboard(App(editor, "micro"));
            if (line < 1) return FileCommand(template, "micro", file);

            bool hasFile = template.Contains("{file}");
            bool hasLine = template.Contains("{line}");
            string command = template
                .Replace("{file}", Quote(file))
                .Replace("{line}", line.ToString(CultureInfo.InvariantCulture));
            if (!hasFile) command += " " + Quote(file);
            if (!hasLine) command += " +" + line;
            return command;
        }
    }
}
