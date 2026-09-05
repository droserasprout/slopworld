using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Git view draws the daemon's changed-file list as a tree. The daemon caps huge working
    // trees before doing expensive diff accounting. It shares the Files view's back pass, while
    // the daemon runs `git` outside the sessions' mount namespaces.
    public static partial class GitView
    {
        const int GitRequestTimeoutMs = 15_000;

        // A directory holds children; a file holds the daemon's row.
        class Node : IContentTreeNode
        {
            public string Name;
            public string Rel;          // relative to the repository root, which is what git takes back
            public string Project;
            public bool IsDir;
            public bool Expanded = true; // a change tree is small, so it opens showing everything
            public List<Node> Kids;
            public Repo Owner;
            public string Status;       // the porcelain pair, files only
            public int Added = -1;      // -1 is "git counted none or was not asked to count"
            public int Deleted = -1;
            public int Depth;

            string IContentTreeNode.Name => Name;
            string IContentTreeNode.Key => Rel;
            string IContentTreeNode.Project => Project;
            bool IContentTreeNode.IsDirectory => IsDir;
            int IContentTreeNode.Depth => Depth;
            bool IContentTreeNode.CanExpand => IsDir;
            bool IContentTreeNode.Loading => false;
            string IContentTreeNode.Error => null;
            bool IContentTreeNode.More => false;
            IEnumerable<IContentTreeNode> IContentTreeNode.Children => Kids;
        }

        // One project's answer, as it stands. Keyed by project name for the reason the files
        // view keys its roots that way: two projects on one directory are two headings, and
        // renaming a project is a heading that has gone.
        class Repo
        {
            public string Project;      // whose heading this is, so a row can name it in an errand
            public string Dir;          // the project's directory, which is what was asked about
            public string Root;         // the repository's, which may be above it
            public string Branch;
            public bool IsRepo = true;
            public bool Loading;
            public string Error;
            public bool Asked;          // whether an answer has ever landed
            public bool Truncated;      // the daemon capped both the tree and its partial summary
            public int Changed, Added, Deleted;
            public Node Tree;
            public HashSet<string> Shut = new HashSet<string>();  // folded directories, by Rel

            // The same changes as the tree, flat and by relative path, with the porcelain pair
            // for each. The tree is for drawing; this is for answering the files view, which
            // asks about one absolute path at a time and would otherwise walk a tree per row
            // per frame.
            public Dictionary<string, string> Changes = new Dictionary<string, string>();
        }

        static readonly Dictionary<string, Repo> Repos = new Dictionary<string, Repo>();

        // Which project headings are rolled up. The files view keeps its own set for the same
        // reason: a tree's shape lives no longer than the process, and the agents view's folds
        // are about agents.
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
            foreach (var group in new TreeSource().Groups())
            {
                if (folded) Shut.Add(group.Key);
                var repo = (Repo)group.Value;
                repo.Shut.Clear();
                if (folded && repo.Tree != null) FoldDirectories(repo.Tree, repo.Shut);
            }
        }

        static void FoldDirectories(Node node, HashSet<string> shut)
        {
            if (node.IsDir && node.Depth > 0) shut.Add(node.Rel);
            if (node.Kids == null) return;
            foreach (var child in node.Kids) FoldDirectories(child, shut);
        }

        // The change the reader is looking at, and the ephemeral session paging its diff.
        // The tree owns the selected row; the project remains part of its identity.
        static readonly Pager Viewer = new Pager();

        // And which of the two things about that change it is showing: the diff, or the file
        // the diff is about. The row and its buttons open different things about the same
        // path, so "click the one already open and its pane comes back" has to be about the
        // one that was clicked.
        static RowAct _showing;

        static readonly ContentTreeView Tree = new ContentTreeView(new TreeSource());

        sealed class TreeSource : ContentTreeSource
        {
            public override IList<ContentTreeGroup> Groups()
            {
                var groups = new List<ContentTreeGroup>();
                foreach (var project in ViewChrome.Projects())
                {
                    var repo = Get(project);
                    groups.Add(new ContentTreeGroup(project, project, repo.Dir, repo, repo.Tree));
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

            public override float DrawGroupTail(Rect row, ContentTreeGroup group, float right)
            {
                var repo = (Repo)group.Value;
                if (Shut.Contains(group.Key) && repo.IsRepo && repo.Changed > 0)
                {
                    GUI.color = UiWidgets.Dim;
                    var count = new Rect(row.width * 0.5f, row.y,
                        right - row.width * 0.5f, row.height);
                    UiWidgets.RowLabel(count, repo.Changed.ToString(), TextAnchor.MiddleRight);
                    GUI.color = UiWidgets.Faint;
                    return count.x - 4f;
                }
                return right;
            }

            public override float GroupBodyHeight(ContentTreeGroup group) =>
                UiWidgets.TinyRowH;

            public override float DrawGroupBody(float width, float y, ContentTreeGroup group) =>
                Body(width, y, (Repo)group.Value);

            public override GroupAct GroupActions(ContentTreeGroup group)
            {
                var repo = (Repo)group.Value;
                var acts = GroupAct.Refresh;
                if (repo.IsRepo && repo.Error == null && repo.Changed > 0)
                    acts |= GroupAct.Diff;
                return acts;
            }

            public override void GroupAction(ContentTreeGroup group, GroupAct action)
            {
                switch (action)
                {
                    case GroupAct.Diff: DiffAll(group.Key); break;
                    case GroupAct.Refresh: Fetch(group.Key); break;
                }
            }

            public override bool IsExpanded(IContentTreeNode node) =>
                !((Node)node).Owner.Shut.Contains(((Node)node).Rel);

            public override void ToggleNode(IContentTreeNode node)
            {
                var git = (Node)node;
                if (!git.Owner.Shut.Remove(git.Rel)) git.Owner.Shut.Add(git.Rel);
            }

            public override RowAct Actions(IContentTreeNode node) => Acts((Node)node);

            public override float DrawRowTail(Rect row, IContentTreeNode node, float right) =>
                RowTail(row, (Node)node, right);

            public override string RowTooltip(IContentTreeNode node)
            {
                var git = (Node)node;
                return git.IsDir ? null : $"{git.Rel}\n\n{Says(git)}";
            }

            public override void Open(IContentTreeNode node) => GitView.Open((Node)node,
                ((Node)node).Owner);
            public override void Action(IContentTreeNode node, RowAct action) =>
                GitView.Act((Node)node, ((Node)node).Owner, action);
            public override List<FloatMenuOption> GroupMenu(ContentTreeGroup group) =>
                HeadMenu(group.Key, (Repo)group.Value);
            public override List<FloatMenuOption> RowMenu(IContentTreeNode node) =>
                GitView.RowMenu((Node)node, ((Node)node).Owner);
        }

        // ------------------------------------------------------------------ asking

        // Every project's tree read again. This is what the refresh button does, and what
        // arriving in the view does - a working tree changes under this column all day and
        // nothing tells it so, the agents being the ones doing the changing.
        public static void Refresh()
        {
            foreach (var name in ViewChrome.Projects()) Fetch(name);
        }

        // Arriving in the view. Separate from Refresh only in what it does not do: a tree
        // already asked about is left alone until the reader asks again, so switching back
        // and forth across a slow repository does not restart the read every time.
        public static void Entered()
        {
            foreach (var name in ViewChrome.Projects())
                if (!Get(name).Asked) Fetch(name);
        }

        static Repo Get(string project)
        {
            var dir = SessionHub.Instance.Project(project)?.Dir ?? "";

            // A project whose directory moved is a different repository under the same
            // heading, and what was known about the old one is not about this one.
            if (Repos.TryGetValue(project, out var repo) && repo.Dir == dir) return repo;

            repo = new Repo { Project = project, Dir = dir };
            Repos[project] = repo;
            return repo;
        }

        static void Fetch(string project)
        {
            var repo = Get(project);
            if (repo.Loading || string.IsNullOrEmpty(repo.Dir)) return;

            repo.Loading = true;
            repo.Error = null;

            string dir = repo.Dir;
            DaemonClient.Get("/api/git?path=" + System.Uri.EscapeDataString(dir),
                j =>
                {
                    repo.Loading = false;
                    repo.Asked = true;
                    // The project moved, or was pointed somewhere else, while this was in
                    // flight; the answer is about a directory nothing is showing.
                    if (repo.Dir != dir) return;

                    repo.IsRepo = j["repo"].AsBool();
                    repo.Changes.Clear();
                    if (!repo.IsRepo)
                    {
                        repo.Tree = null;
                        repo.Changed = repo.Added = repo.Deleted = 0;
                        return;
                    }

                    repo.Root = j["root"].AsString();
                    repo.Branch = j["branch"].AsString();
                    repo.Changed = j["changed"].AsInt();
                    repo.Added = j["added"].AsInt();
                    repo.Deleted = j["deleted"].AsInt();
                    repo.Truncated = j["truncated"].AsBool(false);
                    repo.Tree = Fold(repo, j["files"]);
                },
                msg =>
                {
                    repo.Loading = false;
                    repo.Asked = true;
                    if (repo.Dir != dir) return;
                    repo.Error = msg;
                    // The tree goes with it: what is drawn is the error alone, and a stale
                    // tree under a message about why it could not be read is two answers. The
                    // flat table goes for the same reason - the files view would otherwise
                    // offer a diff off a reading that failed.
                    repo.IsRepo = false;
                    repo.Root = null;
                    repo.Branch = null;
                    repo.Changed = repo.Added = repo.Deleted = 0;
                    repo.Truncated = false;
                    repo.Tree = null;
                    repo.Changes.Clear();
                }, null, GitRequestTimeoutMs);
        }

        // The flat list of changed paths, folded into the tree it describes. The daemon sends
        // them sorted, so a directory's rows arrive together and the walk down never has to
        // look back; every interior node is a directory because something under it changed,
        // which is the whole difference between this tree and the files view's. A truncated
        // answer deliberately remains a valid partial tree.
        static Node Fold(Repo repo, JVal files)
        {
            var root = new Node
            {
                IsDir = true,
                Depth = -1,
                Kids = new List<Node>(),
                Rel = "",
                Project = repo.Project,
                Owner = repo,
            };

            foreach (var f in files.Items)
            {
                string rel = f["path"].AsString();
                if (string.IsNullOrEmpty(rel)) continue;
                repo.Changes[rel] = f["status"].AsString();

                var parts = rel.Split('/');
                var at = root;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    string seg = parts[i];
                    // The last kid is the one to grow: the list is sorted, so anything else
                    // under this directory has already been added.
                    var last = at.Kids.Count > 0 ? at.Kids[at.Kids.Count - 1] : null;
                    if (last == null || !last.IsDir || last.Name != seg)
                    {
                        last = new Node
                        {
                            Name = seg,
                            IsDir = true,
                            Kids = new List<Node>(),
                            Depth = at.Depth + 1,
                            Rel = at.Rel.Length == 0 ? seg : at.Rel + "/" + seg,
                            Project = repo.Project,
                            Owner = repo,
                        };
                        at.Kids.Add(last);
                    }
                    at = last;
                }

                at.Kids.Add(new Node
                {
                    Name = parts[parts.Length - 1],
                    Rel = rel,
                    IsDir = false,
                    Depth = at.Depth + 1,
                    Status = f["status"].AsString(),
                    // Null where git counted nothing (binary or a capped response). Kept apart
                    // from zero, which is a real count and a different row.
                    Added = f["added"].IsNull ? -1 : f["added"].AsInt(),
                    Deleted = f["deleted"].IsNull ? -1 : f["deleted"].AsInt(),
                    Project = repo.Project,
                    Owner = repo,
                });
            }

            // A directory holding one directory holding one file is three rows saying one
            // thing. Squashed the way every git client squashes it: `slopd/src` is one row.
            // Depth is written afterwards rather than kept in step through the squash, a
            // collapsed chain moving everything under it up by however long the chain was.
            Squash(root);
            Depths(root, -1);
            return root;
        }

        // A directory with exactly one child, itself a directory, becomes one row named for
        // both. Bottom-up, so a chain of any length collapses in one pass.
        static void Squash(Node dir)
        {
            foreach (var kid in dir.Kids)
                if (kid.IsDir) Squash(kid);

            for (int i = 0; i < dir.Kids.Count; i++)
            {
                var kid = dir.Kids[i];
                while (kid.IsDir && kid.Kids.Count == 1 && kid.Kids[0].IsDir)
                {
                    var only = kid.Kids[0];
                    only.Name = kid.Name + "/" + only.Name;
                    kid = only;
                }
                dir.Kids[i] = kid;
            }
        }

        static void Depths(Node dir, int depth)
        {
            dir.Depth = depth;
            if (dir.Kids == null) return;
            foreach (var kid in dir.Kids) Depths(kid, depth + 1);
        }

    }
}
