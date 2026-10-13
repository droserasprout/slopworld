using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Node = SlopWorld.GitStore.Node;
using Repo = SlopWorld.GitStore.Repo;

namespace SlopWorld
{
    // Git view draws the daemon's changed-file list as a tree. The daemon caps huge working
    // trees before doing expensive diff accounting. It shares the Files view's back pass, while
    // the daemon runs `git` outside the sessions' mount namespaces.
    public static partial class GitView
    {
        static readonly GitStore Store = new GitStore(
            SidebarScopes.Directory,
            SidebarScopes.Enabled,
            () => Time.realtimeSinceStartup,
            (url, ok, fail, timeout) =>
                DaemonClient.Get<Wire.GitResult>(url, ok, fail, null, timeout)
        );

        // Default open; folded directories are navigation state, independent of snapshots.
        static readonly Dictionary<string, HashSet<string>> Folds =
            new Dictionary<string, HashSet<string>>();

        static HashSet<string> Shut(Repo repo)
        {
            if (!Folds.TryGetValue(repo.Project, out var shut))
                Folds[repo.Project] = shut = new HashSet<string>();
            return shut;
        }

        static GitView()
        {
            Store.Changed += BumpTree;
            SidebarScopes.Changed += () =>
            {
                Store.Invalidate(key => SidebarScopes.Find(key) != null);
                foreach (
                    var key in Folds.Keys.Where(key => SidebarScopes.Find(key) == null).ToList()
                )
                    Folds.Remove(key);
                CancelPendingDiff();
                BumpTree();
            };
        }

        static bool ScopeUnfolded(string key) =>
            !TreeController.IsGroupCollapsed(key)
            && !TreeController.IsGroupCollapsed(SidebarScopes.Find(key)?.ProjectKey);

        static PagerTabs Viewers => FileReaders.Tabs;

        public static bool AllFolded
        {
            get { return TreeController.AllFolded; }
        }

        public static void SetAllFolded(bool folded)
        {
            var groups = TreeController.Groups();
            TreeController.SetAllFolded(folded);
            foreach (var group in groups)
            {
                var repo = (Repo)group.Value;
                if (repo == null)
                    continue;
                Shut(repo).Clear();
                if (folded && repo.Tree != null)
                    FoldDirectories(repo.Tree, Shut(repo));
            }
            BumpTree();
        }

        static void FoldDirectories(Node node, HashSet<string> shut)
        {
            if (node.IsDir && node.Depth > 0)
                shut.Add(node.Rel);
            if (node.Kids == null)
                return;
            foreach (var child in node.Kids)
                FoldDirectories(child, shut);
        }

        static IList<ContentTreeGroup> BuildGroups()
        {
            var groups = new List<ContentTreeGroup>();
            foreach (var project in SidebarScopes.EnabledScopes().GroupBy(s => s.ProjectKey))
            {
                var first = project.First();
                groups.Add(new ContentTreeGroup(first.ProjectKey, first.Project, "", null, null));
                foreach (var scope in project)
                {
                    var repo = Get(scope.Key);
                    groups.Add(
                        new ContentTreeGroup(scope.Key, scope.Label, scope.Path, repo, repo.Tree)
                        {
                            ParentKey = scope.ProjectKey,
                        }
                    );
                }
            }
            TreeController.SyncGroups(SidebarScopes.GroupKeys);
            return groups;
        }

        static readonly ContentTreeController TreeController = new ContentTreeController(
            BuildGroups
        );
        static readonly ContentTreeView Tree = new ContentTreeView(
            new TreeSource(),
            TreeController
        );

        internal static int TreeRevision => TreeController.Revision;

        static void BumpTree()
        {
            TreeController.Bump();
        }

        sealed class TreeSource
            : ContentTreeSource,
                IContentTreeGroupExtras,
                IContentTreeRowActions,
                IContentTreeSelection
        {
            public override Color RowIconColor(IContentTreeNode node) => Color.white;

            public override Color RowLabelColor(IContentTreeNode node) =>
                node.IsDirectory ? UiTheme.Lead : UiTheme.Name;

            public override int Revision =>
                unchecked(
                    TreeController.Revision * 397
                    ^ (int)SessionHub.Instance.SessionsVersion
                    ^ SessionHub.Instance.ProjectsRevision
                    ^ SidebarScopes.Revision
                );

            public override string EmptyReason => SidebarScopes.EmptyReason();

            public override IList<ContentTreeGroup> Groups() => TreeController.Groups();

            public override bool IsGroupCollapsed(ContentTreeGroup group) =>
                TreeController.IsGroupCollapsed(group);

            public override void ToggleGroup(ContentTreeGroup group)
            {
                if (TreeController.ToggleGroup(group))
                    foreach (var scope in SidebarScopes.EnabledScopes())
                        if (
                            (scope.Key == group.Key || scope.ProjectKey == group.Key)
                            && ScopeUnfolded(scope.Key)
                        )
                            Fetch(scope.Key);
            }

            public override string GroupTooltip(ContentTreeGroup group) => group.Path;

            public float DrawGroupTail(Rect row, ContentTreeGroup group, float right)
            {
                var repo = (Repo)group.Value;
                if (
                    repo != null
                    && TreeController.IsGroupCollapsed(group)
                    && repo.IsRepo
                    && repo.Changed > 0
                )
                {
                    GUI.color = UiTheme.Dim;
                    var count = new Rect(
                        row.width * 0.5f,
                        row.y,
                        right - row.width * 0.5f,
                        row.height
                    );
                    UiText.RowLabel(
                        count,
                        repo.Changed.ToString(CultureInfo.CurrentCulture),
                        TextAnchor.MiddleRight
                    );
                    GUI.color = UiTheme.Faint;
                    return count.x - 4f;
                }
                return right;
            }

            public float GroupBodyHeight(ContentTreeGroup group) =>
                group.Value == null ? 0f : UiTheme.TinyRowH;

            public float DrawGroupBody(float width, float y, ContentTreeGroup group) =>
                Body(width, y, (Repo)group.Value);

            public GroupAct GroupActions(ContentTreeGroup group)
            {
                var repo = (Repo)group.Value;
                if (repo == null)
                    return GroupAct.None;
                var acts = GroupAct.Refresh;
                if (repo.IsRepo && repo.Error == null && repo.Changed > 0)
                    acts |= GroupAct.Diff;
                return acts;
            }

            public void GroupAction(ContentTreeGroup group, GroupAct action)
            {
                switch (action)
                {
                    case GroupAct.Diff:
                        DiffAll(group.Key);
                        break;
                    case GroupAct.Refresh:
                        Fetch(group.Key);
                        break;
                }
            }

            public override bool IsExpanded(IContentTreeNode node) =>
                !Shut(((Node)node).Owner).Contains(((Node)node).Rel);

            public override void ToggleNode(IContentTreeNode node)
            {
                var git = (Node)node;
                if (!Shut(git.Owner).Remove(git.Rel))
                    Shut(git.Owner).Add(git.Rel);
                BumpTree();
            }

            public RowAct Actions(IContentTreeNode node) => Acts((Node)node);

            public void DoubleClick(IContentTreeNode node)
            {
                var git = (Node)node;
                GitView.LockViewerFile(git.Owner.Project, git.Rel);
            }

            public float DrawRowTail(Rect row, IContentTreeNode node, float right) =>
                RowTail(row, (Node)node, right);

            public string RowTooltip(IContentTreeNode node)
            {
                var git = (Node)node;
                return git.IsDir ? null : $"{git.Rel}\n\n{Says(git)}";
            }

            public override void Open(IContentTreeNode node) =>
                GitView.Open((Node)node, ((Node)node).Owner);

            public void Action(IContentTreeNode node, RowAct action) =>
                GitView.Act((Node)node, ((Node)node).Owner, action);

            public override List<FloatMenuOption> GroupMenu(ContentTreeGroup group) =>
                group.Value == null ? null : HeadMenu(group.Key, (Repo)group.Value);

            public override List<FloatMenuOption> RowMenu(IContentTreeNode node) =>
                GitView.RowMenu((Node)node, ((Node)node).Owner);
        }

        // ------------------------------------------------------------------ asking

        // Read every project tree again when the user refreshes or opens this view.
        // Agents can change a working tree without sending a change notification to this view.
        public static void Refresh()
        {
            foreach (var name in SidebarScopes.EnabledScopes().Select(s => s.Key))
                if (ScopeUnfolded(name))
                    Fetch(name);
        }

        // Files also consumes this cache for its Diff actions.
        public static void Entered() => Refresh();

        internal static void RefreshIfDue()
        {
            if (!SessionHub.Instance.Online)
                return;
            Store.RefreshIfDue(
                SidebarScopes.EnabledScopes().Select(s => s.Key).Where(ScopeUnfolded)
            );
        }

        static Repo Get(string project) => Store.Get(SidebarScopes.Key(project));

        static void Fetch(string project, System.Action refreshed = null) =>
            Store.Fetch(SidebarScopes.Key(project), refreshed);
    }
}
