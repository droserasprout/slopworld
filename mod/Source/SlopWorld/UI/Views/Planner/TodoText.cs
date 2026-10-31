using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SlopWorld
{
    internal enum TodoFilter
    {
        Active,
        Today,
        Upcoming,
        Overdue,
        Completed,
        All,
    }

    internal enum TodoSort
    {
        Due,
        Priority,
        Title,
    }

    // Pure todo.txt projection and field edits. The daemon still owns identity, revisions,
    // and execution. Unchanged lines and unowned metadata are never reconstructed.
    internal sealed class TodoEntry
    {
        internal uint Line;
        internal string Raw,
            Title,
            Priority,
            Created,
            CompletedOn,
            Due,
            Projects,
            Contexts,
            Id,
            TaskId;
        internal bool Completed;
        internal int BodyStart;
        internal List<TodoToken> Tokens;
        internal DateTime? DueDate => TodoText.Date(Due);
        internal string Key => string.IsNullOrEmpty(Id) ? "line:" + Line : Id;
    }

    internal sealed class TodoToken
    {
        internal int Start,
            Length;
        internal string Value,
            Field;
    }

    internal sealed class TodoDraft
    {
        internal string Title,
            Priority,
            Due,
            Projects,
            Contexts;
        internal bool Completed;

        internal TodoDraft(TodoEntry entry)
        {
            Title = entry.Title;
            Priority = entry.Priority;
            Due = entry.Due;
            Projects = entry.Projects;
            Contexts = entry.Contexts;
            Completed = entry.Completed;
        }

        internal bool Changed(TodoEntry entry) =>
            Title != entry.Title
            || Priority != entry.Priority
            || Due != entry.Due
            || Projects != entry.Projects
            || Contexts != entry.Contexts
            || Completed != entry.Completed;

        internal string Error
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Title))
                    return "Give this task a title.";
                if (Title.IndexOf('\r') >= 0 || Title.IndexOf('\n') >= 0)
                    return "A task title must fit on one line.";
                if (TodoText.Words(Title).Any(word => TodoText.Field(word) != "title"))
                    return "Use the project, context and date fields for tags and metadata.";
                if (
                    Priority.Length > 0
                    && (Priority.Length != 1 || Priority[0] < 'A' || Priority[0] > 'Z')
                )
                    return "Priority must be A–Z or None.";
                if (Due.Length > 0 && TodoText.Date(Due) == null)
                    return "Use YYYY-MM-DD for the due date.";
                if (
                    TodoText
                        .Words(Projects + " " + Contexts)
                        .Any(word => word.IndexOf(':') >= 0 || word[0] == '+' || word[0] == '@')
                )
                    return "Separate names with spaces, without +, @ or colons.";
                return null;
            }
        }
    }

    internal static class TodoText
    {
        static readonly Regex Tokens = new Regex(@"\S+", RegexOptions.Compiled);
        static readonly Regex Metadata = new Regex(@"^[^\s:]+:[^\s:]+$", RegexOptions.Compiled);

        internal static string[] Words(string text) =>
            (text ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        internal static DateTime? Date(string value) =>
            DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            )
                ? date
                : (DateTime?)null;

        internal static string Stamp(DateTime date) =>
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        internal static string Field(string token)
        {
            if (token.Length > 1 && token[0] == '+')
                return "projects";
            if (token.Length > 1 && token[0] == '@')
                return "contexts";
            if (token.StartsWith("due:", StringComparison.Ordinal))
                return "due";
            if (token.StartsWith("pri:", StringComparison.Ordinal))
                return "pri";
            return Metadata.IsMatch(token) ? "metadata" : "title";
        }

        internal static List<TodoEntry> Parse(string document)
        {
            var entries = new List<TodoEntry>();
            var lines = document.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (!string.IsNullOrWhiteSpace(line))
                    entries.Add(ParseLine(line, checked((uint)i)));
            }
            return entries;
        }

        static TodoEntry ParseLine(string line, uint index)
        {
            var tokens = Tokens
                .Matches(line)
                .Cast<Match>()
                .Select(match => new TodoToken
                {
                    Start = match.Index,
                    Length = match.Length,
                    Value = match.Value,
                    Field = Field(match.Value),
                })
                .ToList();
            var entry = new TodoEntry
            {
                Line = index,
                Raw = line,
                Completed = line.StartsWith("x ", StringComparison.Ordinal),
                Priority = "",
                Created = "",
                CompletedOn = "",
                Id = "",
                TaskId = "",
            };
            int at = 0;
            if (entry.Completed)
            {
                at++;
                if (at < tokens.Count && Date(tokens[at].Value) != null)
                    entry.CompletedOn = tokens[at++].Value;
            }
            if (
                at < tokens.Count
                && (at > 0 || tokens[at].Start == 0)
                && tokens[at].Value.Length == 3
                && tokens[at].Value[0] == '('
                && tokens[at].Value[2] == ')'
                && tokens[at].Value[1] >= 'A'
                && tokens[at].Value[1] <= 'Z'
            )
                entry.Priority = tokens[at++].Value.Substring(1, 1);
            if (
                at < tokens.Count
                && (at > 0 || tokens[at].Start == 0)
                && Date(tokens[at].Value) != null
            )
                entry.Created = tokens[at++].Value;
            entry.BodyStart = at < tokens.Count ? tokens[at].Start : line.Length;
            entry.Tokens = tokens.Skip(at).ToList();
            entry.Title = string.Join(
                " ",
                entry.Tokens.Where(token => token.Field == "title").Select(token => token.Value)
            );
            entry.Projects = string.Join(
                " ",
                entry
                    .Tokens.Where(token => token.Field == "projects")
                    .Select(token => token.Value.Substring(1))
            );
            entry.Contexts = string.Join(
                " ",
                entry
                    .Tokens.Where(token => token.Field == "contexts")
                    .Select(token => token.Value.Substring(1))
            );
            entry.Due = Value(entry, "due:");
            entry.Id = Value(entry, "sw-id:");
            entry.TaskId = Value(entry, "sw-task:");
            if (entry.Completed && entry.Priority.Length == 0)
                entry.Priority = Value(entry, "pri:");
            return entry;
        }

        static string Value(TodoEntry entry, string prefix) =>
            entry
                .Tokens.FirstOrDefault(token =>
                    token.Value.StartsWith(prefix, StringComparison.Ordinal)
                )
                ?.Value.Substring(prefix.Length)
            ?? "";

        internal static string Update(
            string document,
            TodoEntry entry,
            TodoDraft draft,
            DateTime today
        )
        {
            if (draft.Error != null)
                throw new ArgumentException(draft.Error);
            if (!draft.Changed(entry))
                return document;
            var changes = new Dictionary<string, string>();
            if (draft.Title != entry.Title)
                changes["title"] = draft.Title.Trim();
            if (draft.Due != entry.Due)
                changes["due"] = draft.Due.Length == 0 ? "" : "due:" + draft.Due;
            if (draft.Projects != entry.Projects)
                changes["projects"] = string.Join(
                    " ",
                    Words(draft.Projects).Select(word => "+" + word)
                );
            if (draft.Contexts != entry.Contexts)
                changes["contexts"] = string.Join(
                    " ",
                    Words(draft.Contexts).Select(word => "@" + word)
                );
            // A leading date (or an x/priority with no prefix) would be parsed as
            // structure. Supply a creation date to keep such an edited title literal.
            string first = Words(draft.Title).FirstOrDefault() ?? "";
            bool titleNeedsDate =
                (
                    draft.Title != entry.Title
                    || draft.Completed != entry.Completed
                    || draft.Priority != entry.Priority
                )
                && entry.Created.Length == 0
                && (
                    Date(first).HasValue
                    || (
                        (draft.Completed || draft.Priority.Length == 0)
                        && Regex.IsMatch(first, @"^\([A-Z]\)$")
                    )
                    || (!draft.Completed && draft.Priority.Length == 0 && first == "x")
                );
            bool prefixChanged =
                titleNeedsDate
                || draft.Completed != entry.Completed
                || draft.Priority != entry.Priority;
            if (prefixChanged)
                changes["pri"] =
                    draft.Completed && draft.Priority.Length > 0 ? "pri:" + draft.Priority : "";
            var text = new StringBuilder();
            if (prefixChanged)
            {
                if (draft.Completed)
                    text.Append("x ")
                        .Append(
                            entry.Completed && entry.CompletedOn.Length > 0
                                ? entry.CompletedOn
                                : Stamp(today)
                        )
                        .Append(' ');
                else if (draft.Priority.Length > 0)
                    text.Append('(').Append(draft.Priority).Append(") ");
                if (entry.Created.Length > 0 || titleNeedsDate)
                    text.Append(entry.Created.Length > 0 ? entry.Created : Stamp(today))
                        .Append(' ');
            }
            else
                text.Append(entry.Raw.Substring(0, entry.BodyStart));
            int cursor = entry.BodyStart;
            var written = new HashSet<string>();
            foreach (var token in entry.Tokens)
            {
                text.Append(entry.Raw.Substring(cursor, token.Start - cursor));
                if (!changes.TryGetValue(token.Field, out var replacement))
                    text.Append(token.Value);
                else if (written.Add(token.Field))
                    text.Append(replacement);
                cursor = token.Start + token.Length;
            }
            text.Append(entry.Raw.Substring(cursor));
            foreach (var pair in changes)
                if (!written.Contains(pair.Key) && pair.Value.Length > 0)
                    text.Append(' ').Append(pair.Value);
            return Replace(document, entry, text.ToString());
        }

        internal static string Replace(string document, TodoEntry entry, string replacement)
        {
            int start = 0;
            for (uint i = 0; i < entry.Line; i++)
            {
                int next = document.IndexOf('\n', start);
                if (next < 0)
                    throw new InvalidOperationException("The task changed. Reload before editing.");
                start = next + 1;
            }
            int end = document.IndexOf('\n', start);
            if (end < 0)
                end = document.Length;
            int bodyEnd = end > start && document[end - 1] == '\r' ? end - 1 : end;
            if (document.Substring(start, bodyEnd - start) != entry.Raw)
                throw new InvalidOperationException("The task changed. Reload before editing.");
            return document.Substring(0, start) + replacement + document.Substring(bodyEnd);
        }

        internal static string Add(string document, string title, string project, DateTime today)
        {
            var draft = new TodoDraft(ParseLine("New task", 0))
            {
                Title = title,
                Projects = project,
            };
            if (draft.Error != null)
                throw new ArgumentException(draft.Error);
            string newline = document.Contains("\r\n") ? "\r\n" : "\n";
            string prefix =
                document.Length > 0 && !document.EndsWith("\n", StringComparison.Ordinal)
                    ? newline
                    : "";
            return document
                + prefix
                + Stamp(today)
                + " "
                + title.Trim()
                + string.Concat(Words(project).Select(word => " +" + word))
                + newline;
        }

        internal static bool Matches(
            TodoEntry entry,
            TodoFilter filter,
            string project,
            string search,
            DateTime today
        )
        {
            if (project.Length > 0 && !Words(entry.Projects).Contains(project))
                return false;
            if (
                search.Length > 0
                && (entry.Title + " " + entry.Projects + " " + entry.Contexts).IndexOf(
                    search,
                    StringComparison.OrdinalIgnoreCase
                ) < 0
            )
                return false;
            switch (filter)
            {
                case TodoFilter.Today:
                    return !entry.Completed
                        && entry.DueDate.HasValue
                        && entry.DueDate.Value.Date <= today.Date;
                case TodoFilter.Overdue:
                    return !entry.Completed
                        && entry.DueDate.HasValue
                        && entry.DueDate.Value.Date < today.Date;
                case TodoFilter.Upcoming:
                    return !entry.Completed
                        && entry.DueDate.HasValue
                        && entry.DueDate.Value.Date > today.Date;
                case TodoFilter.Completed:
                    return entry.Completed;
                case TodoFilter.All:
                    return true;
                default:
                    return !entry.Completed;
            }
        }

        internal static string Group(TodoEntry entry, DateTime today)
        {
            if (entry.Completed)
                return "Completed";
            if (!entry.DueDate.HasValue)
                return "No due date";
            if (entry.DueDate.Value.Date < today.Date)
                return "Overdue";
            if (entry.DueDate.Value.Date == today.Date)
                return "Today";
            return "Upcoming";
        }

        internal static IEnumerable<TodoEntry> Sort(IEnumerable<TodoEntry> entries, TodoSort sort)
        {
            var active = entries.OrderBy(entry => entry.Completed);
            if (sort == TodoSort.Title)
                return active
                    .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Line);
            if (sort == TodoSort.Priority)
                return active
                    .ThenBy(
                        entry => entry.Priority.Length == 0 ? "~" : entry.Priority,
                        StringComparer.Ordinal
                    )
                    .ThenBy(entry => entry.DueDate ?? DateTime.MaxValue)
                    .ThenBy(entry => entry.Line);
            return active
                .ThenBy(entry => entry.DueDate ?? DateTime.MaxValue)
                .ThenBy(
                    entry => entry.Priority.Length == 0 ? "~" : entry.Priority,
                    StringComparer.Ordinal
                )
                .ThenBy(entry => entry.Line);
        }
    }
}
