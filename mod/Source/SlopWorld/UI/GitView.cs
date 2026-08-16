using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Git view draws the daemon's whole changed-file list as a tree. It shares the Files view's
    // back pass, while the daemon runs `git` outside the sessions' mount namespaces.
    public static class GitView
    {
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
            public int Added = -1;      // -1 is "git counted none": a binary file
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
            public bool Truncated;      // the summary is whole, but the drawn tree hit the daemon cap
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
                    GUI.color = SlopWidgets.Dim;
                    var count = new Rect(row.width * 0.5f, row.y,
                        right - row.width * 0.5f, row.height);
                    SlopWidgets.RowLabel(count, repo.Changed.ToString(), TextAnchor.MiddleRight);
                    GUI.color = SlopWidgets.Faint;
                    return count.x - 4f;
                }
                return right;
            }

            public override float GroupBodyHeight(ContentTreeGroup group) =>
                SlopWidgets.TinyRowH;

            public override float DrawGroupBody(float width, float y, ContentTreeGroup group) =>
                Body(width, y, (Repo)group.Value);

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
            SlopClient.Get("/api/git?path=" + System.Uri.EscapeDataString(dir),
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
                });
        }

        // The flat list of changed paths, folded into the tree it describes. The daemon sends
        // them sorted, so a directory's rows arrive together and the walk down never has to
        // look back; every interior node is a directory because something under it changed,
        // which is the whole difference between this tree and the files view's.
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
                    // Null where git counted nothing - a binary file. Kept apart from zero,
                    // which is a real count and a different row.
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

        // The state line and then the tree, or the state line alone where there is no tree to
        // draw. One row either way, which is what Measure counts.
        static float Body(float width, float y, Repo repo)
        {
            if (repo.Loading && repo.Tree == null)
                return ViewChrome.Note(width, y, 0, "...", SlopWidgets.Faint);
            if (repo.Error != null)
                return ViewChrome.Note(width, y, 0, repo.Error, SlopWidgets.Bad);
            if (!repo.Asked)
                return ViewChrome.Note(width, y, 0, "not read yet", SlopWidgets.Faint);
            if (!repo.IsRepo)
                return ViewChrome.Note(width, y, 0, "not a git repository", SlopWidgets.Faint);

            y = State(width, y, repo);
            return y;
        }

        // The branch, and the shortstat beside it: `git diff --shortstat` said in the room a
        // column this narrow has, which is a figure each rather than a sentence.
        static float State(float width, float y, Repo repo)
        {
            var r = new Rect(0f, y, width, SlopWidgets.TinyRowH);
            float x = SlopWidgets.GapS;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;

            // From the right, so the branch takes whatever is left rather than pushing the
            // figures off the edge - a branch name is the long half of this line.
            float rx = width - SlopWidgets.GapS;
            if (repo.Changed == 0)
            {
                rx = Tail(rx, y, "clean", SlopWidgets.Faint);
            }
            else
            {
                if (repo.Deleted > 0) rx = Tail(rx, y, "-" + repo.Deleted, SlopWidgets.Bad);
                if (repo.Added > 0) rx = Tail(rx, y, "+" + repo.Added, SlopWidgets.Yes);
                rx = Tail(rx, y, repo.Changed + (repo.Truncated ? "*" : ""), SlopWidgets.Dim);
            }

            GUI.color = SlopWidgets.Dim;
            Text.Anchor = TextAnchor.MiddleLeft;
            var branch = new Rect(x, y, Mathf.Max(0f, rx - x - 4f), SlopWidgets.TinyRowH);
            SlopWidgets.RowLabel(branch, repo.Branch ?? "");

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            TooltipHandler.TipRegion(r, repo.Changed == 0
                ? $"{repo.Root}\n\nNothing changed."
                : $"{repo.Root}\n\n{repo.Changed} changed, " +
                  $"{repo.Added} insertions(+), {repo.Deleted} deletions(-)" +
                  (repo.Truncated ? "\n\nThe tree shows the first 2,000 changes." : ""));
            return y + SlopWidgets.TinyRowH;
        }

        // One figure laid out from the right, and the x the next one ends at. The anchor does
        // the measuring, so nothing here has to know how wide a number is.
        static float Tail(float right, float y, string text, Color color)
        {
            // A pixel either side of the measurement: a rect exactly as wide as its own
            // CalcSize clips the last glyph's overhang on a face whose advance is narrower
            // than its ink, which on a number is the whole of what there was to read.
            float w = SlopWidgets.Wide(text) + 2f;
            GUI.color = color;
            SlopWidgets.RowLabel(new Rect(right - w, y, w, SlopWidgets.TinyRowH), text,
                TextAnchor.MiddleRight);
            return right - w - 5f;
        }

        static float RowTail(Rect row, Node node, float right)
        {
            if (node.IsDir) return right;
            if (node.Added < 0)
                right = Tail(right, row.y, "bin", SlopWidgets.Faint);
            else
            {
                if (node.Deleted > 0) right = Tail(right, row.y, "-" + node.Deleted,
                    SlopWidgets.Bad);
                if (node.Added > 0) right = Tail(right, row.y, "+" + node.Added,
                    SlopWidgets.Yes);
            }
            return Tail(right, row.y, Mark(node.Status), MarkColor(node.Status));
        }

        // Every changed file offers Diff; existing files additionally offer Edit and text files
        // offer View.
        static RowAct Acts(Node node)
        {
            if (node.IsDir) return RowAct.None;

            var acts = RowAct.Diff;
            if (!Present(node.Status)) return acts;

            acts |= RowAct.Edit;
            if (FilesView.IsText(node.Name)) acts |= RowAct.View;
            return acts;
        }

        // A path is absent when either worktree deletion or staged deletion is authoritative.
        static bool Present(string status)
        {
            if (string.IsNullOrEmpty(status) || status == "??") return true;
            char staged = status[0], worktree = status.Length > 1 ? status[1] : ' ';
            if (worktree == 'D') return false;
            return !(staged == 'D' && worktree == ' ');
        }

        // The porcelain pair said in one character, because one is what fits: the staged
        // letter where there is one, the unstaged letter otherwise. Which of the two it was
        // is the color's job below.
        static string Mark(string status)
        {
            if (string.IsNullOrEmpty(status)) return "?";
            if (status == "??") return "?";
            char staged = status[0];
            char worktree = status.Length > 1 ? status[1] : ' ';
            return (staged != ' ' && staged != '?' ? staged : worktree).ToString();
        }

        // Staged is the color of a thing that is going somewhere; everything else is the
        // color of a thing that is not. Untracked is neither, and is dimmer than both.
        static Color MarkColor(string status)
        {
            if (string.IsNullOrEmpty(status) || status == "??") return SlopWidgets.Faint;
            if (Unmerged(status)) return SlopWidgets.Bad;
            return status[0] != ' ' ? SlopWidgets.Yes : SlopWidgets.Warn;
        }

        static string Says(Node node)
        {
            string s = node.Status ?? "";
            if (s == "??") return "untracked";
            if (Unmerged(s)) return "unmerged";

            char staged = s.Length > 0 ? s[0] : ' ';
            char worktree = s.Length > 1 ? s[1] : ' ';
            var parts = new List<string>();
            if (staged != ' ') parts.Add(Word(staged) + ", staged");
            if (worktree != ' ') parts.Add(Word(worktree) + " since");
            return parts.Count == 0 ? s : string.Join("; ", parts.ToArray());
        }

        // Git uses DD and AA as unmerged pairs without a U character. Keep all seven
        // porcelain conflict pairs red and describe them as one state rather than two
        // ordinary staged/worktree operations.
        static bool Unmerged(string status)
        {
            return status == "AA" || status == "DD" || status == "AU" || status == "UD" ||
                status == "UA" || status == "DU" || status == "UU";
        }

        static string Word(char c)
        {
            switch (c)
            {
                case 'M': return "modified";
                case 'A': return "added";
                case 'D': return "deleted";
                case 'R': return "renamed";
                case 'C': return "copied";
                case 'T': return "type changed";
                case 'U': return "unmerged";
                default: return c.ToString();
            }
        }

        public static void Draw(Rect body) => Tree.Draw(body);
        // Folding the change tree only changes navigation; keep the active diff visible while
        // the reader opens or closes directories around it.
        public static void Clicks() => Tree.Clicks();

        public static bool IsViewerSession(string session) => Viewer.Session == session;

        // Left on a change: mark it and read its diff. Clicking the one already open just
        // brings its pane back - a focus change is a *different* row, and only that replaces
        // the pager.
        static void Open(Node node, Repo repo)
        {
            bool same = Tree.IsSelected(node) && _showing == RowAct.Diff;
            Tree.Select(node);
            if (!same || !Viewer.Reopen()) Diff(node, repo);
        }

        // One of the hover strip's three, done. The diff is what the row itself does; the
        // other two are the file the change is about, opened in the same errands the files
        // view opens them in - a reader in this view should not have to cross to the other one
        // to read the file a diff is about.
        static void Act(Node node, Repo repo, RowAct act)
        {
            string abs = Abs(repo, node);

            switch (act)
            {
                case RowAct.View:
                    // The file reader is owned by Files, not by this tree.
                    AgentSidebar.ShowFiles();
                    FilesView.ViewFile(repo.Project, abs, "view-" + node.Name);
                    break;

                case RowAct.Edit:
                    AgentSidebar.ShowFiles();
                    FilesView.EditFile(repo.Project, abs, "edit-" + node.Name);
                    break;

                case RowAct.Diff:
                    Open(node, repo);
                    break;
            }
        }

        // A row's path on disk. Git states everything relative to the repository root, which
        // is not always the project's directory, and every errand takes a path.
        static string Abs(Repo repo, Node node) =>
            (repo.Root ?? "").TrimEnd('/') + "/" + node.Rel;

        // ------------------------------------------------------------------ what the files
        //                                                                     view asks
        //
        // FilesView queries only the already-read repository cache; this path never fetches.

        // The repository as it stands for a project, or null where there is none to speak of:
        // never read, read and refused, not a repository, or read about a directory this
        // project no longer points at.
        static Repo Known(string project)
        {
            if (string.IsNullOrEmpty(project)) return null;
            var dir = SessionHub.Instance.Project(project)?.Dir ?? "";
            if (!Repos.TryGetValue(project, out var repo)) return null;
            return repo.Dir == dir && repo.IsRepo && repo.Error == null ? repo : null;
        }

        // The path relative to the repository root, or null for anything outside it.
        static string RelOf(Repo repo, string abs)
        {
            string root = repo.Root ?? "";
            if (root.Length == 0 || string.IsNullOrEmpty(abs)) return null;
            if (root == "/")
                return abs.StartsWith("/") && abs.Length > 1 ? abs.Substring(1) : null;
            root = root.TrimEnd('/');
            return abs.StartsWith(root + "/") ? abs.Substring(root.Length + 1) : null;
        }

        public static bool Changed(string project, string abs)
        {
            var repo = Known(project);
            string rel = repo == null ? null : RelOf(repo, abs);
            return rel != null && repo.Changes.ContainsKey(rel);
        }

        // Returns a diff command for the cached path, without opening a pager.
        public static string DiffFor(string project, string abs)
        {
            var repo = Known(project);
            string rel = repo == null ? null : RelOf(repo, abs);
            if (rel == null || !repo.Changes.TryGetValue(rel, out var status)) return null;
            return DiffCmd(repo, rel, status);
        }

        // ------------------------------------------------------------------ menus

        static List<FloatMenuOption> HeadMenu(string project, Repo repo)
        {
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Refresh", () => Fetch(project)),
            };

            if (repo.IsRepo && repo.Error == null && !string.IsNullOrEmpty(repo.Root))
            {
                opts.Add(new FloatMenuOption("Copy repository path", () => Copy(repo.Root)));
                // The whole tree's diff, in the same pager one file's opens in. Tracked as
                // the viewer, so the next row clicked replaces it.
                if (repo.Changed > 0)
                    opts.Add(new FloatMenuOption("Diff all", () =>
                    {
                        ClearSelection();
                        Viewer.Open(project, DiffCmd(repo, null, null), "diff-" + project);
                    }));
            }

            FilesView.AddFileActions(opts, project, repo.Dir, project);

            opts.Add(new FloatMenuOption("Terminal (host)", () =>
                SessionHub.Instance.RunHostShell(project,
                    session => TerminalWindow.Open(session), SlopWidgets.Fail)));

            return opts;
        }

        static List<FloatMenuOption> RowMenu(Node node, Repo repo)
        {
            string project = repo.Project;
            string abs = Abs(repo, node);
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Copy path", () => Copy(abs)),
                new FloatMenuOption("Copy relative path", () => Copy(node.Rel)),
            };

            FilesView.AddFileActions(opts, project, abs, node.Name);

            if (!node.IsDir && Present(node.Status))
                opts.Add(new FloatMenuOption("Edit", () =>
                {
                    AgentSidebar.ShowFiles();
                    FilesView.EditFile(project, abs, "edit-" + node.Name);
                }));

            opts.Add(new FloatMenuOption("Diff", () =>
            {
                // A directory's diff is every change under it and no one row's, so nothing
                // is marked; a file's is the row itself.
                if (node.IsDir) ClearSelection();
                else Tree.Select(node);
                Diff(node, repo);
            }));

            return opts;
        }

        static void Copy(string text) =>
            SlopClient.Post("/api/clipboard", "{" + $"\"text\":{JVal.Q(text)}" + "}",
                _ => Messages.Message($"SlopWorld: copied {text}", MessageTypeDefOf.SilentInput,
                    false),
                SlopWidgets.Fail);

        // ------------------------------------------------------------------ the diff
        //
        // Diffs run in one Pager session through an ephemeral agent in the project sandbox.

        // Public for FilesView: a diff opened from the files tree still belongs to Git and
        // must therefore use this pager, so the resulting ghost appears in the Git tab.
        public static void OpenDiff(string project, string abs, string label)
        {
            var repo = Known(project);
            string rel = repo == null ? null : RelOf(repo, abs);
            if (repo == null || rel == null || !repo.Changes.TryGetValue(rel, out var status))
            {
                SlopWidgets.Fail($"nothing to diff in {System.IO.Path.GetFileName(abs)}");
                return;
            }
            Tree.SelectKey(ContentTreeView.SelectionKey(project, rel));
            _showing = RowAct.Diff;
            Viewer.Open(project, DiffCmd(repo, rel, status), label);
        }

        static void Diff(Node node, Repo repo)
        {
            Tree.Select(node);
            _showing = RowAct.Diff;
            Viewer.Open(repo.Project, DiffCmd(repo, node.Rel, node.Status), "diff-" + node.Name);
        }

        // Use git's pager because daemon errands are argv, not shell pipelines. Force color
        // and LESS=R: git's default X avoids the alternate screen, preventing the pane from
        // sending wheel input to less; F would quit on short diffs. `-C` anchors paths when a
        // project points below the repository root.
        static string DiffCmd(Repo repo, string rel, string status)
        {
            string git = "git -C " + Pager.Quote(repo.Root) +
                " -c " + Pager.Quote("core.pager=LESS=R " + Pager.PipePager) + " --paginate";

            // An untracked or newly added file has no useful HEAD blob to diff against.
            // `--no-index` against the empty file shows its current contents as added; it also
            // works for staged additions in a repository with no commit yet.
            if (status == "??" || (status != null && status.IndexOf('A') >= 0))
                return git + " diff --color=always --no-index -- /dev/null " + Pager.Quote(rel);

            // Against HEAD rather than the index or the worktree alone: what a reader means by
            // "what changed here" is both halves at once, which is also what the counts beside
            // the row are.
            string cmd = git + " diff --color=always HEAD";
            return rel == null ? cmd : cmd + " -- " + Pager.Quote(rel);
        }

        // ------------------------------------------------------------------ lifecycle

        public static void ReleaseViewer()
        {
            ClearSelection();
            Viewer.Release();
        }

        public static void CloseViewerIf(string session) => Viewer.CloseIf(session);

        static void ClearSelection()
        {
            Tree.ClearSelection();
            _showing = RowAct.None;
        }
    }
}
