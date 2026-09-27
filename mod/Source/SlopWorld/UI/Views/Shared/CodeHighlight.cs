using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SlopWorld
{
    // Profile-local themes only decorate recognized tools. Custom wrappers keep their command.
    public static class CodeHighlight
    {
        static readonly Regex Executable = new Regex(@"^\s*(?:'([^']*)'|""([^""]*)""|(\S+))");

        public static string Engine(string command)
        {
            var match = Executable.Match(command ?? "");
            string path = match.Groups[1].Success ? match.Groups[1].Value
                : match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            switch (Path.GetFileName(path))
            {
                case "highlight": return "highlight";
                case "pygmentize": return "pygments";
                case "bat": return "bat";
                default: return "";
            }
        }

        public static string Theme(ModSettings settings, string engine)
        {
            switch (engine)
            {
                case "highlight": return settings.codeHighlightTheme ?? "";
                case "pygments": return settings.codePygmentsTheme ?? "";
                case "bat": return settings.codeBatTheme ?? "";
                default: return "";
            }
        }

        public static string Command(string command, ModSettings settings)
        {
            string engine = Engine(command), theme = Theme(settings, engine);
            if (theme.Length == 0) return command;
            string option = engine == "highlight" ? "--style=" + PagerCommands.Quote(theme)
                : engine == "bat" ? "--theme=" + PagerCommands.Quote(theme)
                : "-P " + PagerCommands.Quote("style=" + theme);
            // Insert before less's file placeholder (and any preceding option terminator).
            int at = command.IndexOf(" -- ", StringComparison.Ordinal);
            if (at < 0) at = command.IndexOf(" %s", StringComparison.Ordinal);
            return at < 0 ? command + " " + option : command.Insert(at, " " + option);
        }

        public static Wire.HighlightReq Request(string text, string language)
        {
            string engine = Engine(SessionHub.Instance.Config.Highlighter);
            return new Wire.HighlightReq
            {
                Text = text, Language = language, Engine = engine,
                Theme = Theme(ModEntry.Instance.settings, engine)
            };
        }

        public static string Revision => Command(SessionHub.Instance.Config.Highlighter,
            ModEntry.Instance.settings) ?? "";
    }
}
