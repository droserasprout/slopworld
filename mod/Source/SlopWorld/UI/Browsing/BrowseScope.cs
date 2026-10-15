using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public enum BrowseScopeSelection
    {
        None,
        Some,
        All,
    }

    // Scope keys are internal navigation identities, never project filter names or wire paths.
    // Keeping the project ID in the key lets retained readers resolve a renamed project.
    public sealed class BrowseScope
    {
        public string ProjectId,
            Project,
            Worktree,
            Name,
            Path,
            Phase,
            Branch,
            Error;

        internal BrowseScope Copy() => (BrowseScope)MemberwiseClone();

        public string Key => Identity(ProjectId, Worktree);
        public string ProjectKey => Identity(ProjectId, "");
        public string Label => Worktree == "main" ? "Main checkout" : Name;
        public bool Ready => Phase == "ready" && !string.IsNullOrEmpty(Path);

        public static string Identity(string projectId, string worktree) =>
            "scope/"
            + Uri.EscapeDataString(projectId ?? "")
            + "/"
            + Uri.EscapeDataString(worktree ?? "main");

        public static bool IsKey(string value) =>
            value != null && value.StartsWith("scope/", StringComparison.Ordinal);

        public static string WorktreeOf(string value) =>
            IsKey(value) ? Uri.UnescapeDataString(value.AsSpan(value.LastIndexOf('/') + 1)) : "";

        public static string ProjectIdOf(ProjectInfo project) => project.Id;

        public static ProjectInfo ProjectOf(string value, IEnumerable<ProjectInfo> projects)
        {
            if (!IsKey(value))
                return projects.FirstOrDefault(p => p.Name == value);
            int end = value.LastIndexOf('/');
            if (end < 6)
                return null;
            string id = Uri.UnescapeDataString(value.AsSpan(6, end - 6));
            return projects.FirstOrDefault(p => ProjectIdOf(p) == id);
        }
    }

    // Missing settings mean Main enabled and registered worktrees opt-in. Store exceptions,
    // including disabled Main, without coupling them to the existing project-name filter.
    // A project key (with an empty worktree ID) means All worktrees, including future ones.
    public sealed class BrowseScopeChoices
    {
        readonly HashSet<string> _exceptions;

        public BrowseScopeChoices(string saved) =>
            _exceptions = new HashSet<string>((saved ?? "").Split('\n').Where(s => s.Length > 0));

        public bool Chosen(BrowseScope scope)
        {
            if (AllChosen(scope.ProjectId))
                return true;
            bool enabledByDefault = scope.Worktree == "main";
            bool hasOverride = _exceptions.Contains(scope.Key);
            return hasOverride ? !enabledByDefault : enabledByDefault;
        }

        public void Toggle(BrowseScope scope)
        {
            if (!scope.Ready)
                return;
            if (AllChosen(scope.ProjectId))
            {
                ClearProject(scope.ProjectId);
                // Like All projects, choosing one item narrows All to that item.
                _exceptions.Add(BrowseScope.Identity(scope.ProjectId, "main"));
                Select(scope);
                return;
            }
            if (!_exceptions.Remove(scope.Key))
                _exceptions.Add(scope.Key);
        }

        public bool AllChosen(string projectId) =>
            _exceptions.Contains(BrowseScope.Identity(projectId, ""));

        public void SelectAll(string projectId)
        {
            ClearProject(projectId);
            _exceptions.Add(BrowseScope.Identity(projectId, ""));
        }

        void ClearProject(string projectId)
        {
            string prefix = BrowseScope.Identity(projectId, "");
            _exceptions.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));
        }

        // Creation replies carry committed identities before the catalog refresh arrives.
        public void Select(BrowseScope scope)
        {
            if (AllChosen(scope.ProjectId))
                return;
            if (scope.Worktree == "main")
                _exceptions.Remove(scope.Key);
            else
                _exceptions.Add(scope.Key);
        }

        public string Save() =>
            string.Join("\n", _exceptions.OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }
}
