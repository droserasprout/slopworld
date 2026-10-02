using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Coordinates sidebar navigation, drawing and row actions. Directory requests and native
    // readers belong to the store/viewer; ContentTreeView owns geometry and hit testing.
    sealed class FilesBrowser
    {
        public FilesStore Store { get; }
        public FilesViewerController Viewer { get; }
        public FilesActions Actions { get; }
        readonly ContentTreeView _tree;

        public FilesBrowser()
        {
            Store = new FilesStore();
            _tree = new ContentTreeView(new TreeSource(this), Store.Controller);
            Viewer = new FilesViewerController(_tree.SelectKey, _tree.ClearSelection);
            Actions = new FilesActions(Store, Viewer);
            Store.Revealed += node => _tree.RevealKey(ContentTreeView.SelectionKey(node.Project, node.Relative));
            SidebarScopes.Changed += Store.ScopesChanged;
        }

        public void Draw(Rect body, bool anchorBoundary)
        {
            if (!SmoothScroll.WheelOnly)
            {
                Store.RefreshIfDue();
                GitView.RefreshIfDue();
                Viewer.RefreshReadersIfDue();
            }
            _tree.Draw(body, anchorBoundary);
        }

        public void Clicks() => _tree.Clicks();

        public void FocusDirectory(string path, string label)
        {
            if (string.IsNullOrEmpty(path)) return;
            Viewer.ReleaseViewer();
            _tree.ClearSelection();
            Store.FocusDirectory(path, label);
            _tree.JumpTo(Vector2.zero);
            AgentSidebar.ShowWithoutHistory(SidebarTab.Files);
        }

        sealed class TreeSource : ContentTreeSource, IContentTreeLoader,
            IContentTreeRowActions, IContentTreeSelection
        {
            readonly FilesBrowser _browser;
            public TreeSource(FilesBrowser browser) { _browser = browser; }
            FilesStore Store => _browser.Store;

            public override Color RowIconColor(IContentTreeNode node) =>
                ((FileNode)node).Gitignored ? UiTheme.Dim : Color.white;

            public override Color RowLabelColor(IContentTreeNode node) =>
                ((FileNode)node).Gitignored ? UiTheme.Dim :
                    (node.IsDirectory ? UiTheme.Lead : UiTheme.Name);

            public override int Revision => unchecked(Store.Controller.Revision * 397
                ^ (int)SessionHub.Instance.SessionsVersion
                ^ SessionHub.Instance.ProjectsRevision ^ SidebarScopes.Revision);

            public override string EmptyReason => SidebarScopes.EmptyReason();
            public override IList<ContentTreeGroup> Groups() => Store.Controller.Groups();

            public override bool IsGroupCollapsed(ContentTreeGroup group) =>
                Store.Controller.IsGroupCollapsed(group);

            public override void ToggleGroup(ContentTreeGroup group)
            {
                Store.ToggleGroup(group);
            }

            public override string GroupTooltip(ContentTreeGroup group) => group.Path;

            public void EnsureLoaded(IContentTreeNode node)
            {
                Store.EnsureLoaded((FileNode)node);
            }

            public override bool IsExpanded(IContentTreeNode node) => ((FileNode)node).Expanded;

            public override void ToggleNode(IContentTreeNode node)
            {
                Store.ToggleNode((FileNode)node);
            }

            public RowAct Actions(IContentTreeNode node) => FilesBrowser.Acts((FileNode)node);
            public float DrawRowTail(Rect row, IContentTreeNode node, float right) => right;
            public string RowTooltip(IContentTreeNode node) => null;
            public override void Open(IContentTreeNode node) => _browser.Viewer.Open((FileNode)node);
            public void DoubleClick(IContentTreeNode node) =>
                _browser.Viewer.LockViewerFile(((FileNode)node).Project, ((FileNode)node).Path);
            public void Action(IContentTreeNode node, RowAct action) =>
                _browser.Act((FileNode)node, action);

            public override List<FloatMenuOption> GroupMenu(ContentTreeGroup group) =>
                group.Root == null ? null : _browser.Actions.Menu((FileNode)group.Root, group.Value as string);

            public override List<FloatMenuOption> RowMenu(IContentTreeNode node) =>
                _browser.Actions.Menu((FileNode)node);
        }

        // Text files offer View/Edit. Changed files additionally offer Diff. Directories and
        // binary files offer no row actions.
        static RowAct Acts(FileNode node)
        {
            if (node.IsDir) return RowAct.None;
            if (FileTypes.IsImage(node.Name)) return RowAct.View;

            RowAct acts = RowAct.None;
            if (!FileTypes.IsText(node.Name)) return RowAct.None;
            acts |= RowAct.Edit | RowAct.View;
            if (GitView.Changed(node.Project, node.Path)) acts |= RowAct.Diff;
            return acts;
        }

        // Both trees open readers in the shared pane without changing sidebar navigation.
        void Act(FileNode node, RowAct act)
        {
            switch (act)
            {
                case RowAct.View:
                    Viewer.Open(node);
                    break;

                case RowAct.Edit:
                    // Editors belong to Files, including when the row is from Git.
                    Viewer.EditFile(node.Project, node.Path, "edit-" + node.Name);
                    break;

                case RowAct.Diff:
                    // Git supplies the command. The shared collection owns its reader.
                    GitView.OpenDiff(node.Project, node.Path, "diff-" + node.Name);
                    break;
            }
        }

        // Resolve against the session's project root, then open only the ancestor listings
        // needed to reveal the row. No filesystem work happens until the click asks for it.
        public bool FocusPath(string project, string path)
        {
            return FocusPath(project, path, true);
        }

        // History restoration uses the same asynchronous reveal, but must not create a new
        // browser entry while it is applying an existing one.
        public bool FocusLocation(string project, string path)
        {
            return FocusPath(project, path, false);
        }

        bool FocusPath(string project, string path, bool remember)
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

            Store.ClearFocus();
            Viewer.ReleaseViewer();
            if (remember) AgentSidebar.RememberFile(project, path);
            // A focused reveal needs the project heading open in the shared group state.
            Store.Controller.SetGroupCollapsed(project, false);
            var scope = SidebarScopes.Find(project);
            if (scope != null) Store.Controller.SetGroupCollapsed(scope.ProjectKey, false);
            if (AgentSidebar.Filtering && !AgentSidebar.Ticked(info.Name))
                AgentSidebar.ToggleFilter(info.Name);
            AgentSidebar.ShowWithoutHistory(SidebarTab.Files);

            Store.RevealPath(project, parts);
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

    }
}
