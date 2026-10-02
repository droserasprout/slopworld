using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopWorld
{
    // Runtime adapter for the shared catalog. Project names remain the Agents filter contract.
    static partial class SidebarScopes
    {
        static readonly BrowseScopeCatalog Catalog = new BrowseScopeCatalog(AgentSidebar.Passes,
            () => Time.realtimeSinceStartup, Load);
        public static event Action Changed { add { Catalog.Changed += value; } remove { Catalog.Changed -= value; } }
        public static int Revision { get { Update(); return Catalog.Revision; } }
        public static int MenuRevision { get { Update(true); return Catalog.Revision; } }
        public static ProjectInfo Project(string key) => BrowseScope.ProjectOf(key, SessionHub.Instance.Projects);
        public static string ProjectName(string key) => Project(key)?.Name ?? key;
        public static string Label(string key)
        {
            var scope = Find(key);
            return scope != null ? scope.Project + " / " + scope.Label :
                BrowseScope.IsKey(key) ? "Unavailable checkout" : key;
        }
        public static string Worktree(string key) => BrowseScope.WorktreeOf(key);
        public static string Directory(string key) => Find(key)?.Path ?? (BrowseScope.IsKey(key) ? "" : Project(key)?.ExpandedDir) ?? "";
        public static string Relative(string key, string path)
        {
            string root = Directory(key).TrimEnd('/');
            return !string.IsNullOrEmpty(root) && path != null && path.StartsWith(root + "/", StringComparison.Ordinal)
                ? path.Substring(root.Length + 1) : path;
        }
        public static BrowseScope Find(string key) => Catalog.Find(key);
        public static string Key(string project) => BrowseScope.IsKey(project) ? project :
            All(project).FirstOrDefault(s => s.Worktree == "main")?.Key ?? project;
        public static bool Enabled(string key) => Catalog.Enabled(key);
        public static bool Chosen(BrowseScope scope) => Catalog.Chosen(scope);
        public static bool HasWorktrees(string project)
        {
            var p = Project(project);
            return p != null && Catalog.HasWorktrees(BrowseScope.ProjectIdOf(p));
        }
        public static string Error(string project)
        {
            var p = Project(project);
            return p == null ? null : Catalog.Error(BrowseScope.ProjectIdOf(p));
        }
        public static List<BrowseScope> All(string project)
        {
            Update();
            var p = Project(project);
            return p == null ? new List<BrowseScope>() : Catalog.All(BrowseScope.ProjectIdOf(p));
        }
        public static List<BrowseScope> EnabledScopes() { Update(); return Catalog.EnabledScopes(); }
        public static string EmptyReason()
        {
            Update();
            return SessionHub.Instance.Online ? Catalog.EmptyReason() : $"daemon {SessionHub.Instance.Status}";
        }
        public static IEnumerable<string> GroupKeys => Catalog.GroupKeys;
        public static void Toggle(BrowseScope scope)
        {
            Catalog.Toggle(scope, saved => Settings.S.sidebarWorktrees = saved);
            Settings.S.Write();
        }
        public static void Invalidate() { Catalog.Invalidate(); Update(); }
        public static void Update() => Update(false);
        static void Update(bool includeHidden) => Catalog.Update(SessionHub.Instance.Projects,
            SessionHub.Instance.ProjectsRevision, Settings.SidebarFilter, Settings.S.sidebarWorktrees ?? "", SessionHub.Instance.Online,
            saved => { Settings.S.sidebarWorktrees = saved; Settings.S.Write(); }, includeHidden);
    }
}
