using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // GitView status rendering, interaction, actions, and viewer lifecycle.
    public static partial class GitView
    {
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

        public static void DiffAll(string project)
        {
            var repo = Known(project);
            if (repo == null || repo.Changed <= 0)
            {
                SlopWidgets.Fail("nothing to diff");
                return;
            }

            ClearSelection();
            Viewer.Open(project, DiffCmd(repo, null, null), "diff-" + project);
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

        // Use delta as git's pager because daemon errands are argv, not shell pipelines. Force
        // color and LESS=R: git's default X avoids the alternate screen, preventing the pane
        // from sending wheel input to less; F would quit on short diffs. `-C` anchors paths
        // when a project points below the repository root.
        static string DiffCmd(Repo repo, string rel, string status)
        {
            string git = "git -C " + Pager.Quote(repo.Root) +
                " -c " + Pager.Quote("core.pager=LESS=R delta --paging=always") + " --paginate";

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
