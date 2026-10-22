using System;
using System.IO;

namespace SlopWorld
{
    // Profile-local themes only decorate recognized tools. Custom wrappers keep their command.
    public static class CodeHighlight
    {
        public static string Engine(string command)
        {
            string path = PagerCommands.Executable(command);
            switch (Path.GetFileName(path))
            {
                case "highlight":
                    return "highlight";
                case "pygmentize":
                    return "pygments";
                case "bat":
                case "batcat":
                    return "bat";
                default:
                    return "";
            }
        }

        public static string Theme(ModSettings settings, string engine)
        {
            switch (engine)
            {
                case "highlight":
                    return settings.codeHighlightTheme ?? "";
                case "pygments":
                    return settings.codePygmentsTheme ?? "";
                case "bat":
                    return ResolveTheme(engine, settings.codeBatTheme);
                default:
                    return "";
            }
        }

        // Empty bat preferences follow the terminal palette in every reader and preview.
        public static string ResolveTheme(string engine, string theme) =>
            engine == "bat" && string.IsNullOrEmpty(theme) ? "ansi" : theme ?? "";

        public static string Command(string command, ModSettings settings)
        {
            string engine = Engine(command),
                theme = Theme(settings, engine);
            if (theme.Length == 0)
                return command;
            string option =
                engine == "highlight" ? "--style=" + PagerCommands.Quote(theme)
                : engine == "bat" ? "--theme=" + PagerCommands.Quote(theme)
                : "-P " + PagerCommands.Quote("style=" + theme);
            return PagerCommands.InsertOptions(command, option, "%s");
        }

        public static Wire.HighlightReq Request(
            string text,
            string language,
            string command,
            ModSettings settings
        )
        {
            string engine = Engine(command);
            return new Wire.HighlightReq
            {
                Text = text,
                Language = language,
                Engine = engine,
                Theme = Theme(settings, engine),
            };
        }
    }
}
