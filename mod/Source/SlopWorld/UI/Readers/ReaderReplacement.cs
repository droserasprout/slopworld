namespace SlopWorld
{
    // Immutable launch/source identity is shared by a committed session and its replacement.
    // Pager owns transitions; these values never resolve mutable UI state in delayed callbacks.
    internal sealed class ReaderSource
    {
        internal readonly string Scope,
            Key,
            Path,
            Label,
            Command,
            SourceCommand,
            Intent;
        internal readonly long Line;
        internal readonly bool ReuseFile;

        internal ReaderSource(
            string scope,
            string key,
            string path,
            string label,
            string command,
            string sourceCommand,
            string intent,
            long line = 0,
            bool reuseFile = false
        )
        {
            Scope = scope;
            Key = key;
            Path = string.IsNullOrEmpty(path) ? null : path;
            Label = label;
            Command = command;
            SourceCommand = sourceCommand;
            Intent = intent;
            Line = line;
            ReuseFile = reuseFile;
        }

        internal bool Matches(string scope, string key) => Scope == scope && Key == key;

        internal ReaderSource WithSourceCommand(string command) =>
            new ReaderSource(Scope, Key, Path, Label, Command, command, Intent, Line, ReuseFile);

        internal SessionRunOptions LaunchOptions(bool pinned) =>
            new SessionRunOptions
            {
                Host = true,
                Path = Path ?? "",
                Intent = Intent,
                Reader = new ReaderLaunchOptions
                {
                    Path = Path ?? "",
                    Key = Key,
                    Scope = Scope,
                    Line = Line,
                    Pinned = pinned,
                },
            };
    }

    internal sealed class ReaderSession
    {
        internal readonly string Session;
        internal readonly ReaderSource Source;
        internal string Stamp;

        internal ReaderSession(string session, ReaderSource source, string stamp)
        {
            Session = session;
            Source = source;
            Stamp = stamp;
        }
    }

    internal enum ReaderReplacementKind
    {
        Open,
        Refresh,
        Appearance,
    }

    // Object identity is the completion guard, including cancellation before the first session.
    internal sealed class ReaderReplacement
    {
        internal readonly ReaderSource Source;
        internal readonly ReaderSession Previous;
        internal readonly ReaderReplacementKind Kind;
        internal readonly string Stamp;
        internal readonly System.Action Shown;

        internal ReaderReplacement(
            ReaderSource source,
            ReaderSession previous,
            ReaderReplacementKind kind,
            string stamp,
            System.Action shown
        )
        {
            Source = source;
            Previous = previous;
            Kind = kind;
            Stamp = stamp;
            Shown = shown;
        }
    }
}
