using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Files view is a lazy directory tree rendered from AgentSidebar's back pass; the daemon
    // owns browsing because the game is outside every session's mount namespace.
    public static partial class FilesView
    {
        // A directory is a rung above a file, which is the whole of the distinction this view
        // draws between them; both, and the greys around them, are SlopWidgets'.

        // One directory, once it has been asked about. `Kids` null is "never asked", which is
        // what makes the tree lazy: a project root is a hundred thousand files deep and the
        // column shows thirty rows.
        class Node : IContentTreeNode
        {
            public string Path;
            public string Name;
            public bool IsDir;
            public bool Expanded;
            public bool Loading;
            public string Error;
            public bool More;          // the daemon's cap cut the listing short
            public List<Node> Kids;
            public string Root;        // the project dir this hangs off, for a relative path
            public string Project;     // and the project that owns it, for an errand
            public int Depth;
            // Invalidates a listing already in flight when Reload forgets this node.
            public int ListingVersion;
            public List<System.Action> Loaded;

            string IContentTreeNode.Name => Name;
            string IContentTreeNode.Key => Path;
            string IContentTreeNode.Project => Project;
            bool IContentTreeNode.IsDirectory => IsDir;
            int IContentTreeNode.Depth => Depth;
            bool IContentTreeNode.CanExpand => IsDir && (Kids == null || Kids.Count > 0);
            bool IContentTreeNode.Loading => Loading;
            string IContentTreeNode.Error => Error;
            bool IContentTreeNode.More => More;
            IEnumerable<IContentTreeNode> IContentTreeNode.Children => Kids;
        }

        sealed class TreeSource : ContentTreeSource
        {
            public override IList<ContentTreeGroup> Groups()
            {
                if (_focusedRoot != null)
                    return new List<ContentTreeGroup>
                    {
                        new ContentTreeGroup(_focusedKey, _focusedRoot.Name, _focusedRoot.Path,
                            null, _focusedRoot)
                    };

                var groups = new List<ContentTreeGroup>();
                foreach (var project in ViewChrome.Projects())
                {
                    var root = Root(project);
                    groups.Add(new ContentTreeGroup(project, project, root.Path, project, root));
                }
                return groups;
            }

            public override bool IsGroupCollapsed(ContentTreeGroup group) =>
                Shut.Contains(group.Key);

            public override void ToggleGroup(ContentTreeGroup group)
            {
                if (!Shut.Remove(group.Key)) Shut.Add(group.Key);
            }

            public override string GroupTooltip(ContentTreeGroup group) => group.Path;

            public override void EnsureLoaded(IContentTreeNode node)
            {
                var file = (Node)node;
                if (file.Expanded && file.Kids == null && !file.Loading && file.Error == null)
                    Fetch(file);
            }

            public override bool IsExpanded(IContentTreeNode node) => ((Node)node).Expanded;

            public override void ToggleNode(IContentTreeNode node)
            {
                var file = (Node)node;
                file.Expanded = !file.Expanded;
                // Closing and opening again is the retry when the last request failed.
                file.Error = null;
            }

            public override RowAct Actions(IContentTreeNode node) => Acts((Node)node);
            public override void Open(IContentTreeNode node) => FilesView.Open((Node)node);
            public override void Action(IContentTreeNode node, RowAct action) =>
                FilesView.Act((Node)node, action);

            public override List<FloatMenuOption> GroupMenu(ContentTreeGroup group) =>
                Menu((Node)group.Root, group.Value as string);

            public override List<FloatMenuOption> RowMenu(IContentTreeNode node) =>
                Menu((Node)node);
        }

        // Keyed by project name rather than by directory: two projects on one directory are
        // two headings, and renaming a project is a heading that has gone.
        static readonly Dictionary<string, Node> Roots = new Dictionary<string, Node>();

        // A storage entry is not a project, but it is still a directory the same tree can
        // browse. It temporarily replaces the project roots when Storage hands Files a path.
        static Node _focusedRoot;
        static string _focusedKey;

        // Which project headings are rolled up here. The agents view has its own set in the
        // settings; this one is a tree's shape and lives no longer than the process, the same
        // as every expansion below it.
        static readonly HashSet<string> Shut = new HashSet<string>();

        public static bool AllFolded
        {
            get
            {
                var groups = new TreeSource().Groups();
                return groups.Count > 0 && groups.All(g => Shut.Contains(g.Key));
            }
        }

        public static void SetAllFolded(bool folded)
        {
            Shut.Clear();
            if (folded)
                foreach (var group in new TreeSource().Groups()) Shut.Add(group.Key);
            foreach (var root in Roots.Values) SetExpanded(root, !folded);
            if (_focusedRoot != null) SetExpanded(_focusedRoot, !folded);
        }

        static void SetExpanded(Node node, bool expanded)
        {
            if (node.Depth > 0) node.Expanded = expanded;
            if (node.Kids == null) return;
            foreach (var child in node.Kids)
                if (child.IsDir) SetExpanded(child, expanded);
        }

        // The file the reader is looking at, and the ephemeral session running `less` on it.
        // The tree owns the selected row; the pager owns the ephemeral session showing it.
        static readonly Pager Viewer = new Pager();

        // And which of the two things about that file it is showing: the file, or its diff.
        // The row and its buttons open different things about the same path, so "click the one
        // already open and its pane comes back" has to be about the one that was clicked.
        static RowAct _showing;

        // Extensions `less` would rather not be handed: the viewer is for reading, and an
        // image or a zip in a text pager is a listing nobody asked for. Everything else is
        // text enough to try.
        static readonly HashSet<string> BinaryExt = new HashSet<string>
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".tga",
            ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".opus",
            ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".flv",
            ".pdf", ".ps", ".zip", ".gz", ".tar", ".7z", ".rar", ".xz", ".bz2",
            ".ttf", ".otf", ".woff", ".woff2", ".eot",
            ".db", ".sqlite", ".sqlite3", ".dll", ".so", ".dylib", ".exe",
            ".bin", ".o", ".a", ".class", ".pyc", ".pyo", ".jar", ".iso", ".img",
        };

        // Public because the git view asks it about the rows it draws: the same question about
        // the same files, and one list of extensions is the point of asking it here.
        public static bool IsText(string name)
        {
            int dot = name.LastIndexOf('.');
            if (dot < 0) return true;               // no extension: read it as text
            return !BinaryExt.Contains(name.Substring(dot).ToLowerInvariant());
        }

        public static bool IsMarkdown(string name)
        {
            int dot = (name ?? "").LastIndexOf('.');
            if (dot < 0) return false;
            string ext = name.Substring(dot).ToLowerInvariant();
            return ext == ".md" || ext == ".markdown" || ext == ".mdx";
        }

        static readonly ContentTreeView Tree = new ContentTreeView(new TreeSource());
        static int _focusVersion;
        const float AutoRefreshSeconds = 2f;
        static float _nextAutoRefresh;

        // Resolve against the session's project root, then open only the ancestor listings
        // needed to reveal the row. No filesystem work happens until the click asks for it.
        public static bool FocusPath(string project, string path)
        {
            var info = SessionHub.Instance.Project(project);
            if (info == null || string.IsNullOrEmpty(info.Dir)) return false;
            string relative = ToProjectRelative(info.Dir, path);
            if (relative == null) return false;
            var parts = NormalizeRelative(relative);
            if (parts == null || parts.Count == 0) return false;

            ClearFocus();
            ReleaseViewer();
            Shut.Remove(project);
            if (AgentSidebar.Filtering && !AgentSidebar.Ticked(project))
                AgentSidebar.ToggleFilter(project);
            AgentSidebar.ShowFiles();

            int version = ++_focusVersion;
            Reveal(Root(project), parts, 0, version);
            return true;
        }

        // An absolute path is only reachable if it sits inside the project's own tree - the
        // one root Files can list - so it is stripped back to a project-relative path here,
        // and a path outside the root is refused so the click falls through. Relative paths
        // pass straight on.
        static string ToProjectRelative(string dir, string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] != '/') return path;

            string root = dir.Replace('\\', '/').TrimEnd('/');
            if (root.Length == 0) return null;
            string abs = path.Replace('\\', '/');
            if (!abs.StartsWith(root, StringComparison.Ordinal)) return null;
            // The root itself, or a real child under it: the separator guards against
            // /home/foo-bar reading as a child of /home/foo.
            if (abs.Length == root.Length) return "";
            if (abs[root.Length] != '/') return null;
            return abs.Substring(root.Length + 1);
        }

        static List<string> NormalizeRelative(string path)
        {
            var parts = new List<string>();
            foreach (string part in (path ?? "").Replace('\\', '/').Split('/'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..")
                {
                    if (parts.Count == 0) return null;
                    parts.RemoveAt(parts.Count - 1);
                }
                else parts.Add(part);
            }
            return parts;
        }

        static void Reveal(Node parent, List<string> parts, int at, int version)
        {
            if (version != _focusVersion) return;
            parent.Expanded = true;
            Fetch(parent, () =>
            {
                if (version != _focusVersion || parent.Kids == null) return;
                var child = parent.Kids.FirstOrDefault(n => n.Name == parts[at]);
                if (child == null) { SlopWidgets.Fail($"path not found: {string.Join("/", parts)}"); return; }
                if (at + 1 < parts.Count)
                {
                    if (!child.IsDir) { SlopWidgets.Fail($"not a directory: {child.Name}"); return; }
                    Reveal(child, parts, at + 1, version);
                    return;
                }
                Tree.RevealKey(ContentTreeView.SelectionKey(project: child.Project, path: child.Path));
            });
        }

        // Everything listed was listed under the old answer about dotfiles, so it is dropped;
        // what the reader arranged - which projects are open, and how deep - is kept.
        public static void Reload()
        {
            ReleaseViewer();
            ClearSelection();
            foreach (var root in Roots.Values) Forget(root);
            if (_focusedRoot != null) Forget(_focusedRoot);
        }

        // The daemon deliberately has no filesystem event stream. Keep the visible tree fresh
        // while Files is open, but leave unopened directories lazy and preserve the old nodes
        // when a listing lands so an external change does not fold the user's tree.
        static void RefreshIfDue()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextAutoRefresh) return;
            _nextAutoRefresh = now + AutoRefreshSeconds;
            if (!SessionHub.Instance.Online) return;

            foreach (var project in ViewChrome.Projects())
                RefreshLoaded(Root(project));
            if (_focusedRoot != null) RefreshLoaded(_focusedRoot);
        }

        static void RefreshLoaded(Node node)
        {
            if (node == null || node.Kids == null || node.Loading) return;

            node.Loading = true;
            node.Error = null;
            string path = node.Path;
            int version = node.ListingVersion;
            SlopClient.Get(
                "/api/browse?files=1&path=" + System.Uri.EscapeDataString(path) +
                "&hidden=" + (Settings.SidebarShowHidden ? "1" : "0"),
                j =>
                {
                    if (node.Path != path || node.ListingVersion != version) return;
                    node.Loading = false;
                    node.Kids = Listed(node, j);
                    node.More = j["truncated"].AsBool();

                    // Walk the fresh children, rather than the old list, so a removed
                    // directory stops generating requests immediately.
                    foreach (var child in node.Kids)
                        if (child.IsDir && child.Kids != null)
                            RefreshLoaded(child);
                },
                msg =>
                {
                    if (node.Path != path || node.ListingVersion != version) return;
                    node.Loading = false;
                    node.Error = msg;
                });
        }

        public static void FocusDirectory(string path, string label)
        {
            if (string.IsNullOrEmpty(path)) return;

            ReleaseViewer();
            ClearSelection();
            _focusedKey = "storage:" + path;
            _focusedRoot = new Node
            {
                Path = path,
                Name = string.IsNullOrEmpty(label) ? path : label,
                IsDir = true,
                Expanded = true,
                Root = path,
                Project = null,
                Depth = 0,
            };
            Tree.JumpTo(Vector2.zero);
            AgentSidebar.ShowFiles();
        }

        public static void ClearFocus()
        {
            _focusedRoot = null;
            _focusedKey = null;
        }

        static void Forget(Node n)
        {
            if (n.Kids != null)
                foreach (var kid in n.Kids)
                    Forget(kid);
            n.Kids = null;
            n.More = false;
            n.Error = null;
            n.Loading = false;
            n.Loaded = null;
            n.ListingVersion++;
            // Not Expanded: that is the shape, and it is what asks for the listing again on
            // the next frame this draws.
        }

        static Node Root(string project)
        {
            var dir = SessionHub.Instance.Project(project)?.Dir ?? "";

            // A project whose directory moved is a different tree under the same heading.
            if (Roots.TryGetValue(project, out var root) && root.Path == dir) return root;

            root = new Node
            {
                Path = dir,
                Name = project,
                IsDir = true,
                // A root is always open; `Shut` is what folds a project, the heading being
                // the agents view's heading rather than a row of this tree. Left false, Rows
                // would return before it ever asked the daemon and every project would draw
                // as an empty one.
                Expanded = true,
                Root = dir,
                Project = project,
                Depth = 0,
            };
            Roots[project] = root;
            return root;
        }

        // Text files offer View/Edit; changed files additionally offer Diff. Directories and
        // binary files offer no row actions.
        static RowAct Acts(Node node)
        {
            if (node.IsDir) return RowAct.None;

            RowAct acts = RowAct.None;
            if (!IsText(node.Name)) return RowAct.None;
            acts |= RowAct.Edit | RowAct.View;
            if (GitView.Changed(node.Project, node.Path)) acts |= RowAct.Diff;
            return acts;
        }

        // ------------------------------------------------------------------ listing

        static void Fetch(Node node, System.Action done = null)
        {
            if (node.Kids != null) { done?.Invoke(); return; }
            if (done != null)
            {
                if (node.Loaded == null) node.Loaded = new List<System.Action>();
                node.Loaded.Add(done);
            }
            if (node.Loading) return;
            node.Loading = true;
            node.Error = null;

            string path = node.Path;
            int version = node.ListingVersion;
            SlopClient.Get(
                "/api/browse?files=1&path=" + System.Uri.EscapeDataString(path) +
                "&hidden=" + (Settings.SidebarShowHidden ? "1" : "0"),
                j =>
                {
                    // The tree was dropped, or the project moved, while this was in flight.
                    if (node.Path != path || node.ListingVersion != version) return;
                    node.Loading = false;

                    node.Kids = Listed(node, j);
                    node.More = j["truncated"].AsBool();
                    var loaded = node.Loaded;
                    node.Loaded = null;
                    if (loaded != null)
                        foreach (var callback in loaded) callback();
                },
                msg =>
                {
                    if (node.Path != path || node.ListingVersion != version) return;
                    node.Loading = false;
                    node.Error = msg;
                    node.Loaded = null;
                });
        }

        static List<Node> Listed(Node parent, JVal j)
        {
            var previous = new Dictionary<string, Node>();
            if (parent.Kids != null)
                foreach (var child in parent.Kids)
                    previous[child.Name] = child;

            var kids = new List<Node>();
            foreach (var d in j["dirs"].Items)
                kids.Add(ReuseOrKid(parent, d.AsString(), true, previous));
            foreach (var f in j["files"].Items)
                kids.Add(ReuseOrKid(parent, f.AsString(), false, previous));
            return kids;
        }

        static Node ReuseOrKid(Node parent, string name, bool dir, Dictionary<string, Node> previous)
        {
            if (previous.TryGetValue(name, out var child) && child.IsDir == dir)
            {
                child.Path = parent.Path.TrimEnd('/') + "/" + name;
                child.Root = parent.Root;
                child.Project = parent.Project;
                child.Depth = parent.Depth + 1;
                return child;
            }
            return Kid(parent, name, dir);
        }

        static Node Kid(Node parent, string name, bool dir) => new Node
        {
            Name = name,
            Path = parent.Path.TrimEnd('/') + "/" + name,
            IsDir = dir,
            Root = parent.Root,
            Project = parent.Project,
            Depth = parent.Depth + 1,
        };

    }
}
