using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Own submitted queries, bounded requests and grouped results. The view keeps editing,
    // selection, reader sessions and geometry; callbacks publish only the current search.
    internal sealed class SearchStore
    {
        readonly BoundedWork Requests = new BoundedWork(4);
        readonly OperationGate Operations = new OperationGate();
        readonly Func<string, bool> _enabled;
        readonly Action<string, Action<Wire.SearchResult>, Action<string>> _request;
        int _pending;
        internal SearchSubmission Submitted { get; private set; }
        internal readonly List<Group> Groups = new List<Group>();
        internal bool Loading { get; private set; }
        internal event Action Changed;

        internal SearchStore(
            Func<string, bool> enabled,
            Action<string, Action<Wire.SearchResult>, Action<string>> request
        )
        {
            _enabled = enabled;
            _request = request;
        }

        internal void Search(
            SearchSubmission submitted,
            IReadOnlyList<BrowseScope> projects,
            bool hidden
        )
        {
            Submitted = submitted;
            long generation = Operations.Begin();
            Groups.Clear();
            string query = submitted.Query;
            _pending = query.Length == 0 ? 0 : projects.Count;
            Loading = _pending > 0;
            Changed?.Invoke();
            if (!Loading)
                return;
            foreach (var scope in projects)
            {
                // Keep delayed result metadata tied to the path submitted to the daemon.
                var p = scope.Copy();
                var group = new Group { Project = p.Project, Worktree = p.Label };
                Groups.Add(group);
                string url =
                    WireProtocol.Routes.Search
                    + "?path="
                    + Uri.EscapeDataString(p.Path)
                    + "&q="
                    + Uri.EscapeDataString(query)
                    + "&regex="
                    + (submitted.Regex ? "1" : "0")
                    + "&case="
                    + (submitted.CaseSensitive ? "1" : "0")
                    + "&word="
                    + (submitted.WholeWord ? "1" : "0")
                    + "&gitignore="
                    + (submitted.IncludeIgnored ? "0" : "1")
                    + "&hidden="
                    + (hidden ? "1" : "0");
                Requests.Add(
                    () => Operations.IsCurrent(generation) && _enabled(p.Key),
                    done =>
                        _request(
                            url,
                            j =>
                            {
                                try
                                {
                                    OnResults(group, p, generation, j);
                                }
                                finally
                                {
                                    done();
                                }
                            },
                            msg =>
                            {
                                try
                                {
                                    OnError(group, generation, msg);
                                }
                                finally
                                {
                                    done();
                                }
                            }
                        )
                );
            }
        }

        internal sealed class Match
        {
            public string Project;
            public string Root;
            public string Path;
            public int Line;
            public int Column;
            public string Text;
        }

        internal sealed class Group
        {
            public string Project;
            public string Worktree;
            public string Error;
            public bool Truncated;
            public readonly List<Match> Matches = new List<Match>();
        }

        void OnResults(Group group, BrowseScope project, long generation, Wire.SearchResult j)
        {
            if (!Operations.IsCurrent(generation))
                return;
            foreach (var row in j.Matches)
                group.Matches.Add(
                    new Match
                    {
                        Project = project.Key,
                        Root = project.Path,
                        Path = row.Path,
                        Line = (int)row.Line,
                        Column = (int)row.Column,
                        Text = row.Text,
                    }
                );
            group.Matches.Sort(
                (a, b) =>
                {
                    int path = string.CompareOrdinal(a.Path, b.Path);
                    return path != 0 ? path : a.Line.CompareTo(b.Line);
                }
            );
            group.Truncated = j.Truncated;
            Done(generation);
        }

        void OnError(Group group, long generation, string msg)
        {
            if (!Operations.IsCurrent(generation))
                return;
            group.Error = msg;
            Done(generation);
        }

        void Done(long generation)
        {
            if (!Operations.IsCurrent(generation))
                return;
            _pending--;
            if (_pending <= 0)
                Loading = false;
            Changed?.Invoke();
        }
    }
}
