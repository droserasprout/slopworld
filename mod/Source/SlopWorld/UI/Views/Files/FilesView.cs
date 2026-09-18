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
        // draws between them; both, and the greys around them, use the shared UI scheme.

        // One directory, once it has been asked about. `Children` null is "never asked", which is
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
            public List<Node> Children;
            public bool HasChildren;
            public bool Gitignored;
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
            bool IContentTreeNode.CanExpand => IsDir && HasChildren;
            bool IContentTreeNode.Loading => Loading;
            string IContentTreeNode.Error => Error;
            bool IContentTreeNode.More => More;
            IEnumerable<IContentTreeNode> IContentTreeNode.Children => Children;
        }

        sealed class BrowseRequest
        {
            public Node Node;
            public string Path;
            public int Version;
            public bool Descend;
        }

        // Files owns directory identity, focus and browse scheduling. The static facade below
        // remains for sidebar callers, while requests and their lifetime now have one owner.
        sealed class FilesStore
        {
            public readonly Dictionary<string, Node> Roots = new Dictionary<string, Node>();
            public Node FocusedRoot;
            public string FocusedKey;
            public int FocusVersion;
            public float NextAutoRefresh;
            public readonly Queue<BrowseRequest> BrowseQueue = new Queue<BrowseRequest>();
            public int BrowseInFlight;
        }

        // Native Markdown rendering state backs the shared reader collection.
        sealed class FilesViewerController
        {
            public readonly PreviewTabs<MarkdownTab> MarkdownTabs =
                new PreviewTabs<MarkdownTab>(() => new MarkdownTab());
            public int MarkdownHeader;
        }

        sealed class TreeSource : ContentTreeSource, IContentTreeLoader,
            IContentTreeRowActions, IContentTreeSelection
        {
            public override Color RowIconColor(IContentTreeNode node) =>
                ((Node)node).Gitignored ? UiTheme.Dim : Color.white;

            public override Color RowLabelColor(IContentTreeNode node) =>
                ((Node)node).Gitignored ? UiTheme.Dim :
                    (node.IsDirectory ? UiTheme.Lead : UiTheme.Name);

            public override int Revision => unchecked(TreeController.Revision * 397
                ^ (int)SessionHub.Instance.SessionsVersion
                ^ SessionHub.Instance.ProjectsRevision);

            public override IList<ContentTreeGroup> Groups() => TreeController.Groups();

            public override bool IsGroupCollapsed(ContentTreeGroup group) =>
                TreeController.IsGroupCollapsed(group);

            public override void ToggleGroup(ContentTreeGroup group)
            {
                bool unfolding = TreeController.ToggleGroup(group);
                if (unfolding) RefreshLoaded((Node)group.Root);
            }

            public override string GroupTooltip(ContentTreeGroup group) => group.Path;

            public void EnsureLoaded(IContentTreeNode node)
            {
                var file = (Node)node;
                if (file.Expanded && file.Children == null && !file.Loading && file.Error == null)
                    Fetch(file);
            }

            public override bool IsExpanded(IContentTreeNode node) => ((Node)node).Expanded;

            public override void ToggleNode(IContentTreeNode node)
            {
                var file = (Node)node;
                file.Expanded = !file.Expanded;
                // Closing and opening again is the retry when the last request failed.
                file.Error = null;
                // A manual expand is a foreground request. Drop queued background work so a
                // single directory does not wait behind the rest of an unfold-all walk.
                CancelQueuedBrowse();
                if (file.Expanded && file.Children != null) RefreshLoaded(file);
                BumpTree();
            }

            public RowAct Actions(IContentTreeNode node) => Acts((Node)node);
            public float DrawRowTail(Rect row, IContentTreeNode node, float right) => right;
            public string RowTooltip(IContentTreeNode node) => null;
            public override void Open(IContentTreeNode node) => FilesView.Open((Node)node);
            public void DoubleClick(IContentTreeNode node) =>
                FilesView.LockViewerFile(((Node)node).Project, ((Node)node).Path);
            public void Action(IContentTreeNode node, RowAct action) =>
                FilesView.Act((Node)node, action);

            public override List<FloatMenuOption> GroupMenu(ContentTreeGroup group) =>
                Menu((Node)group.Root, group.Value as string);

            public override List<FloatMenuOption> RowMenu(IContentTreeNode node) =>
                Menu((Node)node);
        }

        // Keyed by project name rather than by directory: two projects on one directory are
        // two headings, and renaming a project is a heading that has gone.
        static readonly FilesStore Store = new FilesStore();
        static readonly FilesViewerController Viewer = new FilesViewerController();

        static Dictionary<string, Node> Roots => Store.Roots;
        static Node _focusedRoot
        {
            get => Store.FocusedRoot;
            set => Store.FocusedRoot = value;
        }
        static string _focusedKey
        {
            get => Store.FocusedKey;
            set => Store.FocusedKey = value;
        }

        public static bool AllFolded
        {
            get { return TreeController.AllFolded; }
        }

        public static void SetAllFolded(bool folded)
        {
            CancelQueuedBrowse();
            TreeController.SetAllFolded(folded);
            foreach (var root in Roots.Values) SetExpanded(root, !folded);
            if (_focusedRoot != null) SetExpanded(_focusedRoot, !folded);

            // Reopening a project must refresh its cached root and the visible descendants
            // before their old children are trusted. Unopened directories remain lazy.
            if (!folded)
            {
                foreach (var project in ViewChrome.Projects())
                    RefreshLoaded(Root(project));
                if (_focusedRoot != null) RefreshLoaded(_focusedRoot);
            }
            BumpTree();
        }

        static void SetExpanded(Node node, bool expanded)
        {
            if (node.Depth > 0) node.Expanded = expanded;
            if (node.Children == null) return;
            foreach (var child in node.Children)
                if (child.IsDir) SetExpanded(child, expanded);
        }

        // One replaceable preview and any previews the user pinned by double-clicking a
        // routed header. The tree owns selection; each pager owns its ephemeral session.
        static PagerTabs Viewers => FileReaders.Tabs;

        // Markdown is a native content view rather than a daemon session. It adapts its
        // identity and focus operations to the same preview/pinned lifecycle as Pager.
        sealed class MarkdownTab : IPreviewTab
        {
            public MarkdownPreview View;
            public string Header;
            public bool Locked { get; private set; }

            public string Session => Header;
            public string FilePath => View?.Path;
            public bool Alive => View != null;

            public bool Matches(string project, string key) => Alive &&
                View.Project == (project ?? "") && View.Path == key;

            public bool Reopen()
            {
                if (!Alive) return false;
                ShowMarkdown(this);
                return true;
            }

            public bool Lock()
            {
                if (!Alive) return false;
                Locked = true;
                return true;
            }

            public bool LockPreview(string project, string key)
            {
                if (!Matches(project, key) || (!Locked && !Showing(this))) return false;
                Locked = true;
                return true;
            }

            public void Release()
            {
                if (!Alive) return;
                if (Showing(this))
                    Find.WindowStack?.WindowOfType<TerminalWindow>()?.Leave();
                View = null;
                Header = null;
                Locked = false;
            }

            public void CloseIf(string session) { }

            public bool CloseTab(string session)
            {
                if (session == null || session != Header) return false;
                bool showing = Showing(this);
                View = null;
                Header = null;
                Locked = false;
                if (showing)
                    Find.WindowStack?.WindowOfType<TerminalWindow>()?.Leave();
                return true;
            }

            public SessionInfo HeaderInfo()
            {
                return new SessionInfo
                {
                    Name = Header,
                    Project = View.Project,
                    Label = "view-" + System.IO.Path.GetFileName(View.Path),
                    Ephemeral = true,
                    Alive = true,
                };
            }
        }

        static PreviewTabs<MarkdownTab> MarkdownViewers => Viewer.MarkdownTabs;

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

        const float AutoRefreshSeconds = 2f;
        const int MaxConcurrentBrowse = 4;
        static int _focusVersion
        {
            get => Store.FocusVersion;
            set => Store.FocusVersion = value;
        }
        static float _nextAutoRefresh
        {
            get => Store.NextAutoRefresh;
            set => Store.NextAutoRefresh = value;
        }
        static Queue<BrowseRequest> BrowseQueue => Store.BrowseQueue;
        static int BrowseInFlight
        {
            get => Store.BrowseInFlight;
            set => Store.BrowseInFlight = value;
        }

        static IList<ContentTreeGroup> BuildGroups()
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
            // Filtering hides headings temporarily; only catalog removal forgets a fold.
            TreeController.SyncGroups(SessionHub.Instance.Projects.Select(p => p.Name));
            return groups;
        }

        static readonly ContentTreeController TreeController =
            new ContentTreeController(BuildGroups);
        static readonly ContentTreeView Tree = new ContentTreeView(new TreeSource(), TreeController);

        internal static int TreeRevision => TreeController.Revision;

        static void BumpTree()
        {
            TreeController.Bump();
        }

        // Resolve against the session's project root, then open only the ancestor listings
        // needed to reveal the row. No filesystem work happens until the click asks for it.
        public static bool FocusPath(string project, string path)
        {
            return FocusPath(project, path, true);
        }

        // History restoration uses the same asynchronous reveal, but must not create a new
        // browser entry while it is applying an existing one.
        public static bool FocusLocation(string project, string path)
        {
            return FocusPath(project, path, false);
        }

        static bool FocusPath(string project, string path, bool remember)
        {
            var info = SessionHub.Instance.Project(project);
            if (info == null || string.IsNullOrEmpty(info.Dir)) return false;
            string relative = ToProjectRelative(info.ExpandedDir, path);
            if (relative == null) return false;
            var parts = NormalizeRelative(relative);
            if (parts == null || parts.Count == 0) return false;

            ClearFocus();
            ReleaseViewer();
            if (remember) AgentSidebar.RememberFile(project, path);
            // A focused reveal needs the project heading open in the shared group state.
            TreeController.SetGroupCollapsed(project, false);
            if (AgentSidebar.Filtering && !AgentSidebar.Ticked(project))
                AgentSidebar.ToggleFilter(project);
            AgentSidebar.ShowWithoutHistory(SidebarTab.Files);

            int version = ++_focusVersion;
            Reveal(Root(project), parts, 0, version);
            BumpTree();
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

        // Diagnostics printed by a shell are relative to that shell's cwd. Resolve them to an
        // absolute path for Viewer/Pager, while refusing to let a path escape the project tree.
        // `cwd` is supplied by tmux; null keeps the project-root behavior for other callers.
        public static string ResolveProjectPath(string project, string path, string cwd = null)
        {
            var info = SessionHub.Instance.Project(project);
            if (info == null || string.IsNullOrEmpty(info.Dir) || string.IsNullOrEmpty(path))
                return null;
            return PathScan.ResolveProjectPath(info.ExpandedDir, cwd, path);
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
                if (version != _focusVersion || parent.Children == null) return;
                var child = parent.Children.FirstOrDefault(n => n.Name == parts[at]);
                if (child == null) { UiLayout.Fail($"path not found: {string.Join("/", parts)}"); return; }
                if (at + 1 < parts.Count)
                {
                    if (!child.IsDir) { UiLayout.Fail($"not a directory: {child.Name}"); return; }
                    Reveal(child, parts, at + 1, version);
                    return;
                }
                Tree.RevealKey(ContentTreeView.SelectionKey(project: child.Project, path: child.Path));
            });
        }

        // Everything listed will be replaced by a fresh answer about visibility. Keep the old
        // node skeleton while that answer is in flight so the reader's expanded branches survive
        // the reload and the new listing can reuse them.
        public static void Reload()
        {
            CancelQueuedBrowse();
            _nextAutoRefresh = 0f;
            ReleaseViewer();
            ClearSelection();
            foreach (var root in Roots.Values) Forget(root);
            if (_focusedRoot != null) Forget(_focusedRoot);
            BumpTree();
        }

        public static void Entered()
        {
            // Tab focus must not reuse the polling deadline or a cached layout: visible
            // unloaded paths need fetching too, including after a file mutation.
            _nextAutoRefresh = 0f;
            BumpTree();
            RefreshIfDue();
        }

        // The daemon deliberately has no filesystem event stream. Keep the visible tree fresh
        // while Files is open, but leave unopened directories lazy and preserve the old nodes
        // when a listing lands so an external change does not fold the user's tree. A collapsed
        // directory row is still visible, so reread its already-loaded listing too; otherwise a
        // newly-created child can leave the row without a disclosure arrow forever.
        static void RefreshIfDue()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextAutoRefresh) return;
            if (!SessionHub.Instance.Online) return;
            // Do not add another generation while an unfold or refresh is still draining.
            if (BrowseInFlight > 0 || BrowseQueue.Count > 0) return;
            _nextAutoRefresh = now + AutoRefreshSeconds;

            if (_focusedRoot != null)
            {
                if (!TreeController.IsGroupCollapsed(_focusedKey)) RefreshLoaded(_focusedRoot);
                return;
            }
            foreach (var project in ViewChrome.Projects())
                if (!TreeController.IsGroupCollapsed(project)) RefreshLoaded(Root(project));
        }

        static void RefreshLoaded(Node node, bool descend = true)
        {
            if (node == null || node.Children == null || node.Loading) return;

            node.Loading = true;
            node.Error = null;
            BumpTree();
            QueueBrowse(node, descend);
        }

        static void CancelQueuedBrowse()
        {
            while (BrowseQueue.Count > 0)
            {
                var request = BrowseQueue.Dequeue();
                if (request.Node.Path == request.Path &&
                    request.Node.ListingVersion == request.Version)
                    request.Node.Loading = false;
            }
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
            BumpTree();
            AgentSidebar.ShowWithoutHistory(SidebarTab.Files);
        }

        public static void ClearFocus()
        {
            _focusedRoot = null;
            _focusedKey = null;
            BumpTree();
        }

        static void Forget(Node n)
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
            // each loaded listing; an actually unopened directory still has Children == null.
        }

        static Node Root(string project)
        {
            var dir = SessionHub.Instance.Project(project)?.ExpandedDir ?? "";

            // A project whose directory moved is a different tree under the same heading.
            if (Roots.TryGetValue(project, out var root) && root.Path == dir) return root;

            root = new Node
            {
                Path = dir,
                Name = project,
                IsDir = true,
                // A root is always open; the controller's group state folds the project
                // heading rather than this root row. Left false, Rows
                // would return before it ever asked the daemon and every project would draw
                // as an empty one.
                Expanded = true,
                Root = dir,
                Project = project,
                Depth = 0,
            };
            Roots[project] = root;
            BumpTree();
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
            if (node.Children != null) { done?.Invoke(); return; }
            if (done != null)
            {
                if (node.Loaded == null) node.Loaded = new List<System.Action>();
                node.Loaded.Add(done);
            }
            if (node.Loading) return;
            node.Loading = true;
            node.Error = null;
            BumpTree();
            QueueBrowse(node, false);
        }

        static void QueueBrowse(Node node, bool descend)
        {
            BrowseQueue.Enqueue(new BrowseRequest
            {
                Node = node,
                Path = node.Path,
                Version = node.ListingVersion,
                Descend = descend,
            });
            PumpBrowse();
        }

        static void PumpBrowse()
        {
            while (BrowseInFlight < MaxConcurrentBrowse && BrowseQueue.Count > 0)
            {
                var request = BrowseQueue.Dequeue();
                if (!Current(request)) continue;

                BrowseInFlight++;
                DaemonClient.Get<Wire.BrowseResult>(
                    WireProtocol.Routes.Browse + "?files=1&path=" + System.Uri.EscapeDataString(request.Path) +
                    "&hidden=" + (Settings.SidebarShowHidden ? "1" : "0") +
                    "&gitignore=" + (Settings.SidebarShowGitignored ? "0" : "1"),
                    j => CompleteBrowse(request, j, null),
                    msg => CompleteBrowse(request, null, msg));
            }
        }

        static bool Current(BrowseRequest request) =>
            request.Node.Loading && request.Node.Path == request.Path &&
            request.Node.ListingVersion == request.Version;

        static void CompleteBrowse(BrowseRequest request, Wire.BrowseResult j, string error)
        {
            BrowseInFlight--;
            try
            {
                if (!Current(request)) return;

                var node = request.Node;
                node.Loading = false;
                if (j == null)
                {
                    node.Error = error;
                    node.Loaded = null;
                    BumpTree();
                    return;
                }

                node.Children = Listed(node, j);
                node.More = j.Truncated;
                node.HasChildren = node.Children.Count > 0 || node.More;
                BumpTree();
                var loaded = node.Loaded;
                node.Loaded = null;
                if (loaded != null)
                    foreach (var callback in loaded) callback();

                if (!request.Descend) return;
                // Walk the fresh children, rather than the old list. Refresh loaded collapsed
                // children so their visible rows keep accurate disclosure state, but only
                // descend through branches that remain expanded.
                foreach (var child in node.Children)
                    if (child.IsDir && child.Children != null)
                        RefreshLoaded(child, child.Expanded);
            }
            finally
            {
                PumpBrowse();
            }
        }

        static List<Node> Listed(Node parent, Wire.BrowseResult j)
        {
            var previous = new Dictionary<string, Node>();
            if (parent.Children != null)
                foreach (var child in parent.Children)
                    previous[child.Name] = child;

            var children = new List<Node>();
            var empty = new HashSet<string>(j.EmptyDirs,
                StringComparer.Ordinal);
            var ignoredDirs = new HashSet<string>(
                j.GitignoredDirs, StringComparer.Ordinal);
            var ignoredFiles = new HashSet<string>(
                j.GitignoredFiles, StringComparer.Ordinal);
            foreach (var d in j.Dirs)
            {
                string name = d;
                children.Add(ReuseOrChild(parent, name, true, previous, !empty.Contains(name),
                    ignoredDirs.Contains(name)));
            }
            foreach (var f in j.Files)
            {
                string name = f;
                children.Add(ReuseOrChild(parent, name, false, previous, false,
                    ignoredFiles.Contains(name)));
            }
            return children;
        }

        static Node ReuseOrChild(Node parent, string name, bool dir, Dictionary<string, Node> previous,
            bool hasChildren, bool gitignored)
        {
            if (previous.TryGetValue(name, out var child) && child.IsDir == dir)
            {
                child.Path = parent.Path.TrimEnd('/') + "/" + name;
                child.Root = parent.Root;
                child.Project = parent.Project;
                child.Depth = parent.Depth + 1;
                child.HasChildren = hasChildren;
                child.Gitignored = gitignored;
                if (dir && !hasChildren)
                {
                    // A parent refresh can learn that a previously expanded directory was
                    // emptied. Drop its old rows immediately instead of waiting for a click.
                    child.Children = new List<Node>();
                    child.More = false;
                }
                return child;
            }
            return Child(parent, name, dir, hasChildren, gitignored);
        }

        static Node Child(Node parent, string name, bool dir, bool hasChildren, bool gitignored) => new Node
        {
            Name = name,
            Path = parent.Path.TrimEnd('/') + "/" + name,
            IsDir = dir,
            Root = parent.Root,
            Project = parent.Project,
            Depth = parent.Depth + 1,
            HasChildren = hasChildren,
            Gitignored = gitignored,
        };

    }
}
