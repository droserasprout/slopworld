using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Own Library's worktree inventory independently of sidebar choices and row geometry.
    // Retain last successes after failure; replacement/refresh invalidates delayed callbacks.
    internal sealed class WorktreeInventory
    {
        readonly OperationGate _operations = new OperationGate();
        readonly Action<string, Action<Wire.WorktreesReply>, Action<string>> _request;
        string _requestKey;
        internal int Revision { get; private set; }
        internal readonly Dictionary<string, List<WorktreeEntry>> WorktreesByProject =
            new Dictionary<string, List<WorktreeEntry>>();
        internal readonly Dictionary<string, string> WorktreeErrors =
            new Dictionary<string, string>();

        internal WorktreeInventory(
            Action<string, Action<Wire.WorktreesReply>, Action<string>> request
        )
        {
            _request = request;
        }

        internal void Invalidate()
        {
            Revision++;
            _requestKey = null;
            _operations.Invalidate();
        }

        internal sealed class WorktreeEntry
        {
            internal string Project;
            internal Wire.Worktree Tree;
        }

        internal void Update(List<ProjectInfo> projects, int revision)
        {
            string key =
                revision
                + "\n"
                + string.Join(
                    "\n",
                    projects.Select(project => project.Name + "\t" + project.ExpandedDir).ToArray()
                );
            if (_requestKey == key)
                return;

            _requestKey = key;
            Revision++;
            // Retain each project's last successful rows while refreshing or after a failure.
            var live = new HashSet<string>(projects.Select(project => project.Name));
            foreach (
                var name in WorktreesByProject.Keys.Where(name => !live.Contains(name)).ToList()
            )
                WorktreesByProject.Remove(name);
            foreach (var name in WorktreeErrors.Keys.Where(name => !live.Contains(name)).ToList())
                WorktreeErrors.Remove(name);
            long generation = _operations.Begin();
            foreach (var project in projects)
            {
                string name = project.Name;
                _request(
                    name,
                    reply =>
                    {
                        if (!_operations.IsCurrent(generation))
                            return;
                        WorktreeErrors.Remove(name);
                        WorktreesByProject[name] = reply
                            .Worktrees.Select(tree => new WorktreeEntry
                            {
                                Project = name,
                                Tree = tree,
                            })
                            .OrderBy(row => row.Tree.Id == "main" ? 0 : 1)
                            .ThenBy(row => row.Tree.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        Revision++;
                    },
                    error =>
                    {
                        if (_operations.IsCurrent(generation))
                        {
                            WorktreeErrors[name] = name + ": " + error;
                            Revision++;
                        }
                    }
                );
            }
        }
    }
}
