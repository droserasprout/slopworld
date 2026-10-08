using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopWorld
{
    // Owns directory identity, groups, focus and refresh. Browsing callbacks belong to this
    // instance; FilesBrowser owns sidebar navigation and ContentTreeView owns geometry.
    sealed partial class FilesStore
    {
        readonly Dictionary<string, FileNode> _roots = new Dictionary<string, FileNode>();
        FileNode _focusedRoot;
        string _focusedKey;
        int _focusVersion;
        float _nextAutoRefresh;
        const float AutoRefreshSeconds = 2f;

        public FilesStore()
        {
            Controller = new ContentTreeController(BuildGroups);
        }

        public ContentTreeController Controller { get; }
        public event Action<FileNode> Revealed;
        void BumpTree() => Controller.Bump();

        public void ScopesChanged()
        {
            CancelQueuedBrowse();
            foreach (var key in _roots.Keys.ToList())
            {
                Forget(_roots[key]);
                if (SidebarScopes.Find(key) == null) _roots.Remove(key);
            }
            _focusVersion++;
            _nextAutoRefresh = 0f;
            BumpTree();
        }

        public bool AllFolded => Controller.AllFolded;

        public void SetAllFolded(bool folded)
        {
            CancelQueuedBrowse();
            Controller.SetAllFolded(folded);
            foreach (var root in _roots.Values) SetExpanded(root, !folded);
            if (_focusedRoot != null) SetExpanded(_focusedRoot, !folded);

            // Reopening a project must refresh its cached root and the visible descendants
            // before their old children are trusted. Unopened directories remain lazy.
            if (!folded)
            {
                foreach (var project in SidebarScopes.EnabledScopes().Select(s => s.Key))
                    RefreshLoaded(Root(project));
                if (_focusedRoot != null) RefreshLoaded(_focusedRoot);
            }
            BumpTree();
        }

        static void SetExpanded(FileNode node, bool expanded)
        {
            if (node.Depth > 0) node.Expanded = expanded;
            if (node.Children == null) return;
            foreach (var child in node.Children)
                if (child.IsDir) SetExpanded(child, expanded);
        }

        IList<ContentTreeGroup> BuildGroups()
        {
            if (_focusedRoot != null)
                return new List<ContentTreeGroup>
                {
                    new ContentTreeGroup(_focusedKey, _focusedRoot.Name, _focusedRoot.Path,
                        null, _focusedRoot)
                };

            var groups = new List<ContentTreeGroup>();
            foreach (var project in SidebarScopes.EnabledScopes().GroupBy(s => s.ProjectKey))
            {
                var first = project.First();
                groups.Add(new ContentTreeGroup(first.ProjectKey, first.Project, "", null, null));
                foreach (var scope in project)
                    groups.Add(new ContentTreeGroup(scope.Key, ScopeLabel(scope), scope.Path, scope.Key, Root(scope.Key))
                    { ParentKey = scope.ProjectKey });
            }
            Controller.SyncGroups(SidebarScopes.GroupKeys);
            return groups;
        }

        static string ScopeLabel(BrowseScope scope) => scope.Label +
            (string.IsNullOrEmpty(scope.Branch) ? "" : "  · " + scope.Branch);

        // Everything listed will be replaced by a fresh answer about visibility. Keep the old
        // node skeleton while that answer is in flight so the reader's expanded branches survive
        // the reload and the new listing can reuse them.
        public void Reload()
        {
            CancelQueuedBrowse();
            _nextAutoRefresh = 0f;
            foreach (var root in _roots.Values) Forget(root);
            if (_focusedRoot != null) Forget(_focusedRoot);
            BumpTree();
        }

        public void Entered()
        {
            // Tab focus must not reuse the polling deadline or a cached layout: visible
            // unloaded paths need fetching too, including after a file mutation.
            _nextAutoRefresh = 0f;
            BumpTree();
            RefreshIfDue();
        }

        // Poll because the daemon has no filesystem event stream. Preserve existing expansion state.
        // Refresh loaded collapsed directories too, so new children restore their disclosure arrow.
        public void RefreshIfDue()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextAutoRefresh) return;
            if (!SessionHub.Instance.Online) return;
            // Do not add another generation while an unfold or refresh is still draining.
            if (_browseInFlight > 0 || _browseQueue.Count > 0) return;
            _nextAutoRefresh = now + AutoRefreshSeconds;

            if (_focusedRoot != null)
            {
                if (!Controller.IsGroupCollapsed(_focusedKey)) RefreshLoaded(_focusedRoot);
                return;
            }
            foreach (var project in SidebarScopes.EnabledScopes().Select(s => s.Key))
                if (ScopeUnfolded(project)) RefreshLoaded(Root(project));
        }

        bool ScopeUnfolded(string key) => !Controller.IsGroupCollapsed(key) &&
            !Controller.IsGroupCollapsed(SidebarScopes.Find(key)?.ProjectKey);

        public void RefreshLoaded(FileNode node, bool descend = true)
        {
            if (node == null || node.Children == null || node.Loading) return;

            node.Loading = true;
            node.Error = null;
            BumpTree();
            QueueBrowse(node, descend);
        }

        void CancelQueuedBrowse()
        {
            var foreground = new List<BrowseRequest>();
            while (_browseQueue.Count > 0)
            {
                var request = _browseQueue.Dequeue();
                if (request.Node.Path != request.Path || request.Node.ListingVersion != request.Version) continue;
                // Reveal may have joined a queued background refresh. Keep that foreground
                // waiter alive while discarding the remaining recursive background work.
                if (request.Node.Loaded != null && request.Node.Loaded.Count > 0)
                {
                    request.Descend = false;
                    foreground.Add(request);
                }
                else request.Node.Loading = false;
            }
            foreach (var request in foreground) _browseQueue.Enqueue(request);
        }

        public void FocusDirectory(string path, string label)
        {
            if (string.IsNullOrEmpty(path)) return;

            _focusedKey = "storage:" + path;
            _focusedRoot = new FileNode
            {
                Path = path,
                Name = string.IsNullOrEmpty(label) ? path : label,
                IsDir = true,
                Expanded = true,
                Root = path,
                Project = null,
                Depth = 0,
            };
            BumpTree();
        }

        public void ClearFocus()
        {
            _focusedRoot = null;
            _focusedKey = null;
            BumpTree();
        }

        static void Forget(FileNode n)
        {
            if (n.Children != null)
                foreach (var child in n.Children)
                    Forget(child);
            n.More = false;
            n.Error = null;
            n.Loading = false;
            n.Loaded = null;
            n.ListingVersion++;
            // Keep Children as a stale snapshot so Listed can reuse these nodes, including
            // their expansion state. Reload resets the deadline, and the root refresh replaces
            // each loaded listing. An actually unopened directory still has Children == null.
        }

        public FileNode Root(string project)
        {
            project = SidebarScopes.Key(project);
            var dir = SidebarScopes.Directory(project);

            // A renamed checkout keeps its semantic tree state; refresh reconciles the relocated paths.
            if (_roots.TryGetValue(project, out var root))
            {
                if (root.Path != dir)
                {
                    Forget(root);
                    Relocate(root, dir, dir);
                    BumpTree();
                }
                root.Name = SidebarScopes.Find(project)?.Label ?? SidebarScopes.ProjectName(project);
                return root;
            }

            root = new FileNode
            {
                Path = dir,
                Name = SidebarScopes.Find(project)?.Label ?? SidebarScopes.ProjectName(project),
                IsDir = true,
                // Project headings own folding. The root must stay expanded to request its listing.
                Expanded = true,
                Root = dir,
                Project = project,
                Depth = 0,
            };
            _roots[project] = root;
            BumpTree();
            return root;
        }

        static void Relocate(FileNode node, string path, string root)
        {
            node.Path = path;
            node.Root = root;
            if (node.Children != null)
                foreach (var child in node.Children) Relocate(child, path.TrimEnd('/') + "/" + child.Name, root);
        }

        public void ToggleGroup(ContentTreeGroup group)
        {
            if (!Controller.ToggleGroup(group)) return;
            if (group.Root != null) RefreshLoaded((FileNode)group.Root);
            else foreach (var scope in SidebarScopes.EnabledScopes().Where(s => s.ProjectKey == group.Key))
                    if (!Controller.IsGroupCollapsed(scope.Key)) RefreshLoaded(Root(scope.Key));
        }

        public void EnsureLoaded(FileNode node)
        {
            if (node.Expanded && node.Children == null && !node.Loading && node.Error == null)
                Fetch(node);
        }

        public void ToggleNode(FileNode node)
        {
            node.Expanded = !node.Expanded;
            node.Error = null;
            // Foreground expansion should not wait behind a recursive background walk.
            CancelQueuedBrowse();
            if (node.Expanded && node.Children != null) RefreshLoaded(node);
            BumpTree();
        }
    }
}
