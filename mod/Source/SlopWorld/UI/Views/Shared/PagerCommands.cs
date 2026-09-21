using System;
using System.IO;

namespace SlopWorld
{
    public static class PagerCommands
    {
        public static string RelativeFilePath(string root, string path)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return path;
            string prefix = root.TrimEnd('/') + "/";
            return path.StartsWith(prefix, StringComparison.Ordinal) ? path.Substring(prefix.Length) : path;
        }

        // The daemon splits a command line into an argv the way a shell would, so a path with
        // a space in it is two arguments unless it says otherwise. Both views build command
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

        static string Executable(string value, string fallback)
        {
            string command = App(value, fallback);
            int end = command.IndexOfAny(new[] { ' ', '\t' });
            return end < 0 ? command : command.Substring(0, end);
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

            int end = command.IndexOfAny(new[] { ' ', '\t' });
            return end < 0
                ? command + " -clipboard terminal"
                : command.Insert(end, " -clipboard terminal");
        }

        // The env vars for a persistent `less` that pipes every file through `highlight`.
        // `%s` is less's own placeholder for the filename, expanded on every `:e`.
        public static string LessEnv(string highlighter)
        {
            string h = (highlighter ?? "").Trim();
            // Paint from the top instead of scrolling a short first screen into place.
            if (h.Length == 0) return "LESS=-Rc";
            if (!h.Contains("%s")) h += " %s";
            return "LESSOPEN=" + Quote("|" + h) + " LESS=-Rc";
        }

        // Git feeds its diff to the pager on stdin, so it needs the configured command
        // without file/line placeholders or the normal file argument.
        public static string PipePager(string pager) =>
            App(pager, "less").Replace("{file}", "").Replace("{line}", "").Trim();

        public static bool IsPagerCommand(string pager, string command)
        {
            string exe = Executable(pager, "less");
            return command == exe || command.StartsWith(exe + " ") ||
                (command.StartsWith("env ") && command.Contains(" " + exe + " "));
        }

        public static bool IsEditorCommand(string editor, string command)
        {
            string exe = Executable(editor, "micro");
            return command == exe || command.StartsWith(exe + " ");
        }

        public static string FileCommand(string value, string fallback, string file, int line = 0)
        {
            string template = App(value, fallback);
            bool hasFile = template.Contains("{file}");
            bool hasLine = template.Contains("{line}");
            string command = template
                .Replace("{file}", Quote(file))
                .Replace("{line}", (line < 1 ? 1 : line).ToString());
            if (!hasFile)
            {
                if (line > 0 && !hasLine) command += " +" + (line < 1 ? 1 : line);
                command += " -- " + Quote(file);
            }
            return command;
        }

        public static string PagerCommand(string pager, string highlighter, string file, int line = 0) =>
            "env " + LessEnv(highlighter) + " " + FileCommand(pager, "less", file, line);

        // micro's +LINE selector follows the file. FileCommand puts a pager's selector before
        // its `-- FILE`, which makes +LINE look like a buffer name to micro.
        public static string EditorCommand(string editor, string file, int line = 0)
        {
            string template = MicroTerminalClipboard(App(editor, "micro"));
            if (line < 1) return FileCommand(template, "micro", file);

            bool hasFile = template.Contains("{file}");
            bool hasLine = template.Contains("{line}");
            string command = template
                .Replace("{file}", Quote(file))
                .Replace("{line}", line.ToString());
            if (!hasFile) command += " " + Quote(file);
            if (!hasLine) command += " +" + line;
            return command;
        }
    }
}
