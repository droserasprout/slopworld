using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Scope keys are internal navigation identities, never project filter names or wire paths.
    // Keeping the project ID in the key lets retained readers resolve a renamed project.
    public sealed class BrowseScope
    {
        public string ProjectId, Project, Worktree, Name, Path, Phase, Branch, Error;
        public string Key => Identity(ProjectId, Worktree);
        public string ProjectKey => Identity(ProjectId, "");
        public string Label => Worktree == "main" ? "Main checkout" : Name;
        public bool Ready => Phase == "ready" && !string.IsNullOrEmpty(Path);
        public static string Identity(string projectId, string worktree) =>
            "scope/" + Uri.EscapeDataString(projectId ?? "") + "/" + Uri.EscapeDataString(worktree ?? "main");
        public static bool IsKey(string value) => value != null && value.StartsWith("scope/", StringComparison.Ordinal);
        public static string WorktreeOf(string value) => IsKey(value) ? Uri.UnescapeDataString(value.Substring(value.LastIndexOf('/') + 1)) : "";
        public static string ProjectIdOf(ProjectInfo project) => string.IsNullOrEmpty(project.Id) ? "name:" + project.Name : project.Id;
        public static ProjectInfo ProjectOf(string value, IEnumerable<ProjectInfo> projects)
        {
            if (!IsKey(value)) return projects.FirstOrDefault(p => p.Name == value);
            int end = value.LastIndexOf('/');
            if (end < 6) return null;
            string id = Uri.UnescapeDataString(value.Substring(6, end - 6));
            return projects.FirstOrDefault(p => ProjectIdOf(p) == id);
        }
    }

    // Missing settings mean Main enabled and registered worktrees opt-in. Store exceptions,
    // including disabled Main, without coupling them to the existing project-name filter.
    public sealed class BrowseScopeChoices
    {
        readonly HashSet<string> _exceptions;
        public BrowseScopeChoices(string saved) => _exceptions = new HashSet<string>(
            (saved ?? "").Split('\n').Where(s => s.Length > 0));
        public bool Chosen(BrowseScope scope) => (scope.Worktree == "main") != _exceptions.Contains(scope.Key);
        public void Toggle(BrowseScope scope)
        {
            if (!scope.Ready) return;
            if (!_exceptions.Remove(scope.Key)) _exceptions.Add(scope.Key);
        }
        public string Save() => string.Join("\n", _exceptions.OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }
}
