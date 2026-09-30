using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Files view is a lazy directory tree rendered from AgentSidebar's back pass. The daemon
    // owns browsing because the game is outside every session's mount namespace.
    public static partial class FilesView
    {
        // A directory is a rung above a file, which is the whole of the distinction this view
        // draws between them. Both, and the greys around them, use the shared UI scheme.

        // One directory, once it has been asked about. `Children` null is "never asked", which is
        // what makes the tree lazy. A project root is a hundred thousand files deep and the column
        // shows thirty rows.
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
            public string Project;     // stable browsing scope key, resolved to project/worktree at the request boundary
            public int Depth;
            // Invalidates a listing already in flight when Reload forgets this node.
            public int ListingVersion;
            public List<System.Action> Loaded;

            string IContentTreeNode.Name => Name;
            string IContentTreeNode.Key => Relative(this) ?? "";
            string IContentTreeNode.ScopeKey => Project;
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

        // Native content readers share the same preview/pinned lifetime as pagers.
        sealed class FilesViewerController
        {
            public readonly PreviewTabs<NativeTab> NativeTabs =
                new PreviewTabs<NativeTab>(() => new NativeTab());
            public int NativeHeader;
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
                ^ SessionHub.Instance.ProjectsRevision ^ SidebarScopes.Revision);

            public override IList<ContentTreeGroup> Groups() => TreeController.Groups();

            public override bool IsGroupCollapsed(ContentTreeGroup group) =>
                TreeController.IsGroupCollapsed(group);

            public override void ToggleGroup(ContentTreeGroup group)
            {
                bool unfolding = TreeController.ToggleGroup(group);
                if (unfolding)
                {
                    if (group.Root != null) RefreshLoaded((Node)group.Root);
                    else foreach (var scope in SidebarScopes.EnabledScopes().Where(s => s.ProjectKey == group.Key))
                        if (!TreeController.IsGroupCollapsed(scope.Key)) RefreshLoaded(Root(scope.Key));
                }
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
                group.Root == null ? null : Menu((Node)group.Root, group.Value as string);

            public override List<FloatMenuOption> RowMenu(IContentTreeNode node) =>
                Menu((Node)node);
        }

        // Roots use stable project/worktree scope keys; labels and paths may change independently.
        static readonly FilesStore Store = new FilesStore();
        static readonly FilesViewerController Viewer = new FilesViewerController();

        static FilesView()
        {
            SidebarScopes.Changed += () =>
            {
                CancelQueuedBrowse();
                foreach (var key in Roots.Keys.ToList())
                {
                    Forget(Roots[key]);
                    if (SidebarScopes.Find(key) == null) Roots.Remove(key);
                }
                _focusVersion++;
                _nextAutoRefresh = 0f;
                BumpTree();
            };
        }
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
                foreach (var project in SidebarScopes.EnabledScopes().Select(s => s.Key))
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
        // routed header. The tree owns selection. Each pager owns its ephemeral session.
        static PagerTabs Viewers => FileReaders.Tabs;

        // Native content has no daemon session. This tab adapts its
        // identity and focus operations to the same preview/pinned lifecycle as Pager.
        sealed class NativeTab : IPreviewTab
        {
            ContentView _view;
            string _header;
            public string OriginLabel, OriginProject, Project, Path;
            public ContentView View
            {
                get => _view;
                set { _view = value; RoutedSessionRows.Invalidate(); }
            }
            public string Header
            {
                get => _header;
                set { _header = value; RoutedSessionRows.Invalidate(); }
            }
            public bool Locked { get; private set; }

            public string Session => Header;
            public string FilePath => Alive ? Path : null;
            public bool Alive => View != null;

            public bool Matches(string project, string key) => Alive &&
                Project == (project ?? "") && Path == key;

            public bool Reopen()
            {
                if (!Alive) return false;
                TerminalWindow.OpenContent(View);
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
                Path = null;
                Locked = false;
            }

            public void Invalidate() => Release();

            public void CloseIf(string session) { }

            public bool CloseTab(string session)
            {
                if (session == null || session != Header) return false;
                bool showing = Showing(this);
                View = null;
                Header = null;
                Path = null;
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
                    Project = SidebarScopes.Project(Project)?.Name ?? OriginProject,
                    Label = OriginLabel,
                    Ephemeral = true,
                    Alive = true,
                };
            }
        }

        static PreviewTabs<NativeTab> NativeViewers => Viewer.NativeTabs;

        // Extensions `less` would rather not be handed. The viewer is for reading, and an image or
        // a zip in a text pager is a listing nobody asked for. Everything else is text enough to
        // try.
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

        // Public because the git view asks it about the rows it draws. The same question about the
        // same files, and one list of extensions is the point of asking it here.
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

        public static bool IsImage(string name)
        {
            int dot = (name ?? "").LastIndexOf('.');
            if (dot < 0) return false;
            string ext = name.Substring(dot).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg";
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
            foreach (var project in SidebarScopes.EnabledScopes().GroupBy(s => s.ProjectKey))
            {
                var first = project.First();
                groups.Add(new ContentTreeGroup(first.ProjectKey, first.Project, "", null, null));
                foreach (var scope in project)
                    groups.Add(new ContentTreeGroup(scope.Key, ScopeLabel(scope), scope.Path, scope.Key, Root(scope.Key))
                    { ParentKey = scope.ProjectKey });
            }
            TreeController.SyncGroups(SidebarScopes.GroupKeys);
            return groups;
        }

        static string ScopeLabel(BrowseScope scope) => scope.Label +
            (string.IsNullOrEmpty(scope.Branch) ? "" : "  · " + scope.Branch);

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
            var info = SidebarScopes.Project(project);
            if (info == null || string.IsNullOrEmpty(info.Dir)) return false;
            project = SidebarScopes.Key(project);
            var targetScope = SidebarScopes.Find(project);
            if (targetScope == null || !targetScope.Ready) return false;
            if (remember)
            {
                if (!SidebarScopes.Chosen(targetScope)) SidebarScopes.Toggle(targetScope);
                if (!AgentSidebar.Passes(info.Name)) AgentSidebar.ToggleFilter(info.Name);
            }
            if (!SidebarScopes.Enabled(project)) return false;
            string relative = ToProjectRelative(SidebarScopes.Directory(project), path);
            if (relative == null) return false;
            var parts = NormalizeRelative(relative);
            if (parts == null || parts.Count == 0) return false;

            ClearFocus();
            ReleaseViewer();
            if (remember) AgentSidebar.RememberFile(project, path);
            // A focused reveal needs the project heading open in the shared group state.
            TreeController.SetGroupCollapsed(project, false);
            var scope = SidebarScopes.Find(project);
            if (scope != null) TreeController.SetGroupCollapsed(scope.ProjectKey, false);
            if (AgentSidebar.Filtering && !AgentSidebar.Ticked(info.Name))
                AgentSidebar.ToggleFilter(info.Name);
            AgentSidebar.ShowWithoutHistory(SidebarTab.Files);

            int version = ++_focusVersion;
            Reveal(Root(project), parts, 0, version);
            BumpTree();
            return true;
        }

        // Reject absolute paths outside the project so the click can fall through.
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
        // `cwd` is supplied by tmux. Null keeps the project-root behavior for other callers.
        public static string ResolveProjectPath(string project, string path, string cwd = null)
        {
            var info = SidebarScopes.Project(project);
            if (info == null || string.IsNullOrEmpty(info.Dir) || string.IsNullOrEmpty(path))
                return null;
            return PathScan.ResolveProjectPath(SidebarScopes.Directory(project), cwd, path);
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
                Tree.RevealKey(ContentTreeView.SelectionKey(project: child.Project, path: Relative(child)));
            });
        }

        // Everything listed will be replaced by a fresh answer about visibility. Keep the old
        // node skeleton while that answer is in flight so the reader's expanded branches survive
        // the reload and the new listing can reuse them.
        public static void Reload()
        {
            CancelQueuedBrowse();
            _nextAutoRefresh = 0f;
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

        // Poll because the daemon has no filesystem event stream. Preserve existing expansion state.
        // Refresh loaded collapsed directories too, so new children restore their disclosure arrow.
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
            foreach (var project in SidebarScopes.EnabledScopes().Select(s => s.Key))
                if (ScopeUnfolded(project)) RefreshLoaded(Root(project));
        }

        static bool ScopeUnfolded(string key) => !TreeController.IsGroupCollapsed(key) &&
            !TreeController.IsGroupCollapsed(SidebarScopes.Find(key)?.ProjectKey);

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
            // each loaded listing. An actually unopened directory still has Children == null.
        }

        static Node Root(string project)
        {
            project = SidebarScopes.Key(project);
            var dir = SidebarScopes.Directory(project);

            // A renamed checkout keeps its semantic tree state; refresh reconciles the relocated paths.
            if (Roots.TryGetValue(project, out var root))
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

            root = new Node
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
            Roots[project] = root;
            BumpTree();
            return root;
        }

        static void Relocate(Node node, string path, string root)
        {
            node.Path = path;
            node.Root = root;
            if (node.Children != null)
                foreach (var child in node.Children) Relocate(child, path.TrimEnd('/') + "/" + child.Name, root);
        }

        // Text files offer View/Edit. Changed files additionally offer Diff. Directories and
        // binary files offer no row actions.
        static RowAct Acts(Node node)
        {
            if (node.IsDir) return RowAct.None;
            if (IsImage(node.Name)) return RowAct.View;

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
            (request.Node.Project == null || SidebarScopes.Enabled(request.Node.Project)) &&
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
                // The parent's empty check ignores gitignore; prefer a cached filtered listing.
                child.HasChildren = dir && child.Children != null
                    ? child.Children.Count > 0 || child.More
                    : hasChildren;
                child.Gitignored = gitignored;
                if (dir && !hasChildren)
                {
                    // A parent refresh can learn that a previously expanded directory was
                    // emptied. Drop its old rows immediately instead of waiting for a click.
                    child.Children = new List<Node>();
                    child.More = false;
                    child.HasChildren = false;
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
