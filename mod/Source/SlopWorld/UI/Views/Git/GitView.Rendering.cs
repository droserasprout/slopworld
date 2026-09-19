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
                return ViewChrome.Note(width, y, 0, "...", UiTheme.Faint);
            if (repo.Error != null)
                return ViewChrome.Note(width, y, 0, repo.Error, UiTheme.Bad);
            if (!repo.Asked)
                return ViewChrome.Note(width, y, 0, "not read yet", UiTheme.Faint);
            if (!repo.IsRepo)
                return ViewChrome.Note(width, y, 0, "not a git repository", UiTheme.Faint);

            y = State(width, y, repo);
            return y;
        }

        // The branch, and the selected paths' counts beside it: a column this narrow has room
        // for a figure each rather than a sentence.
        static float State(float width, float y, Repo repo)
        {
            var r = new Rect(0f, y, width, UiTheme.TinyRowH);
            float x = UiTheme.GapS;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;

            // From the right, so the branch takes whatever is left rather than pushing the
            // figures off the edge - a branch name is the long half of this line.
            float rx = width - UiTheme.GapS;
            if (repo.Changed == 0)
            {
                rx = Tail(rx, y, "clean", UiTheme.Faint);
            }
            else
            {
                if (repo.Deleted > 0) rx = Tail(rx, y, "-" + repo.Deleted, UiTheme.Bad);
                if (repo.Added > 0) rx = Tail(rx, y, "+" + repo.Added, UiTheme.Yes);
                rx = Tail(rx, y, repo.Changed + (repo.Truncated ? "+" : ""), UiTheme.Dim);
            }

            GUI.color = UiTheme.Dim;
            Text.Anchor = TextAnchor.MiddleLeft;
            var branch = new Rect(x, y, Mathf.Max(0f, rx - x - UiTheme.GapXS),
                UiTheme.TinyRowH);
            UiText.RowLabel(branch, repo.Branch ?? "");

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            TooltipHandler.TipRegion(r, repo.Changed == 0
                ? $"{repo.Root}\n\nNothing changed."
                : repo.Truncated
                    ? $"{repo.Root}\n\nAt least {repo.Changed} changes.\n\n" +
                      "The tree shows the first 2,000 changes."
                    : !repo.CountsComplete
                        ? $"{repo.Root}\n\n{repo.Changed} changed. Line counts unavailable."
                    : $"{repo.Root}\n\n{repo.Changed} changed, " +
                      $"{repo.Added} insertions(+), {repo.Deleted} deletions(-)");
            return y + UiTheme.TinyRowH;
        }

        // One figure laid out from the right, and the x the next one ends at. The anchor does
        // the measuring, so nothing here has to know how wide a number is.
        static float Tail(float right, float y, string text, Color color)
        {
            // A pixel either side of the measurement: a rect exactly as wide as its own
            // CalcSize clips the last glyph's overhang on a face whose advance is narrower
            // than its ink, which on a number is the whole of what there was to read.
            float w = UiTheme.Wide(text) + 2f;
            GUI.color = color;
            UiText.RowLabel(new Rect(right - w, y, w, UiTheme.TinyRowH), text,
                TextAnchor.MiddleRight);
            return right - w - 5f;
        }

        static bool NeedsStage(string status)
        {
            if (string.IsNullOrEmpty(status) || status == "??") return true;
            return status.Length > 1 && status[1] != ' ';
        }

        static bool IsStaged(string status) => !string.IsNullOrEmpty(status)
            && status != "??" && status[0] != ' ';

        static bool Any(Node node, System.Func<string, bool> test)
        {
            if (!node.IsDir) return test(node.Status);
            if (node.Kids == null) return false;
            foreach (var child in node.Kids)
                if (Any(child, test)) return true;
            return false;
        }

        static bool HasStaged(Repo repo)
        {
            foreach (var status in repo.Changes.Values)
                if (IsStaged(status)) return true;
            return false;
        }

        static void GitAction(Repo repo, string command, string notice)
        {
            if (repo == null || !repo.IsRepo || repo.Error != null || string.IsNullOrEmpty(repo.Root))
            {
                UiLayout.Fail("repository is not available");
                return;
            }

            string full = "git -C " + Pager.Quote(repo.Root) + " " + command;
            DaemonClient.Post<Wire.OutputResult>(WireProtocol.Routes.FileAction, new Wire.FileActionReq { Path = repo.Root, Command = full, Host = true },
                _ =>
                {
                    Messages.Message("SlopWorld: " + notice,
                        MessageTypeDefOf.SilentInput, false);
                    Fetch(repo.Project);
                },
                msg => UiLayout.Fail("Git: " + msg));
        }

        static void Stage(Repo repo, string rel) =>
            GitAction(repo, "add -- " + Pager.Quote(rel), "staged " + rel);

        static void Unstage(Repo repo, string rel) =>
            GitAction(repo, "reset -- " + Pager.Quote(rel), "unstaged " + rel);

        static void StageAll(Repo repo) => GitAction(repo, "add --all", "staged all changes");

        static void UnstageAll(Repo repo) =>
            GitAction(repo, "reset -- .", "unstaged all changes");

        public static void StageAll(string project)
        {
            var repo = Known(project);
            if (repo == null || repo.Changed <= 0)
            {
                UiLayout.Fail("nothing to stage");
                return;
            }

            StageAll(repo);
        }

        public static void UnstageAll(string project)
        {
            var repo = Known(project);
            if (repo == null || !HasStaged(repo))
            {
                UiLayout.Fail("no staged changes to unstage");
                return;
            }

            UnstageAll(repo);
        }

        public static void CommitStaged(string project)
        {
            var repo = Known(project);
            if (repo == null || !HasStaged(repo))
            {
                UiLayout.Fail("no staged changes to commit");
                return;
            }

            GitCommitDialog.Open(project);
        }

        public static void Commit(string project, string message)
        {
            var repo = Known(project);
            if (repo == null || !HasStaged(repo))
            {
                UiLayout.Fail("no staged changes to commit");
                return;
            }

            GitAction(repo, "commit -m " + Pager.Quote(message) + " --", "committed changes");
        }

        static float RowTail(Rect row, Node node, float right)
        {
            if (node.IsDir) return right;
            if (node.Added < 0)
            {
                // A capped response omits all numstat values, so null there means unknown,
                // not necessarily binary. Preserve the binary marker for complete answers.
                if (!node.Owner.Truncated)
                    right = Tail(right, row.y, "bin", UiTheme.Faint);
            }
            else
            {
                if (node.Deleted > 0) right = Tail(right, row.y, "-" + node.Deleted,
                    UiTheme.Bad);
                if (node.Added > 0) right = Tail(right, row.y, "+" + node.Added,
                    UiTheme.Yes);
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
            if (string.IsNullOrEmpty(status) || status == "??") return UiTheme.Faint;
            if (Unmerged(status)) return UiTheme.Bad;
            return status[0] != ' ' ? UiTheme.Yes : UiTheme.Warn;
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

        public static void Draw(Rect body, bool anchorBoundary = true)
        {
            if (!SmoothScroll.WheelOnly)
            {
                RefreshIfDue();
                FilesView.RefreshReadersIfDue();
            }
            Tree.Draw(body, anchorBoundary);
        }
        // Folding the change tree only changes navigation; keep the active diff visible while
        // the reader opens or closes directories around it.
        public static void Clicks() => Tree.Clicks();

        public static bool IsViewerSession(string session) => Viewers.IsSession(session);

        public static bool IsViewerLocked(string session) => Viewers.IsLocked(session);

        public static bool LockViewer(string session) => Viewers.Lock(session);

        static string DiffKey(string rel) => "diff:" + rel;

        public static bool LockViewerFile(string project, string rel)
        {
            if (_pendingDiffProject == project && _pendingDiffRel == rel)
            {
                _pinPendingDiff = true;
                return true;
            }
            return Viewers.LockPreview(project, DiffKey(rel));
        }

        public static bool FocusLocation(string project, string rel)
        {
            var repo = Known(project);
            if (repo == null || string.IsNullOrEmpty(rel) || !repo.Changes.ContainsKey(rel))
                return false;
            Tree.RevealKey(ContentTreeView.SelectionKey(project, rel));
            return true;
        }

        // Row selection asks for current content; routed headers only restore focus.
        static void Open(Node node, Repo repo)
        {
            Tree.Select(node);
            Diff(node, repo);
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
                    // Files supplies the reader without changing the active tree.
                    FilesView.ViewFile(repo.Project, abs, "view-" + node.Name);
                    break;

                case RowAct.Edit:
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
            var dir = SessionHub.Instance.Project(project)?.ExpandedDir ?? "";
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
            CancelPendingDiff();
            int request = _diffRequest;
            Fetch(project, () =>
            {
                if (request != _diffRequest) return;
                var repo = Known(project);
                if (repo == null || repo.Changed <= 0)
                {
                    UiLayout.Fail("nothing to diff");
                    return;
                }
                ClearSelection();
                Viewers.ForPreview().Open(project, DiffCmd(repo, null, null), "diff-" + project);
            });
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

                if (repo.Changed > 0)
                    opts.Add(new FloatMenuOption("Stage all", () => StageAll(repo)));
                if (HasStaged(repo))
                {
                    opts.Add(new FloatMenuOption("Unstage all", () => UnstageAll(repo)));
                    opts.Add(new FloatMenuOption("Commit staged...",
                        () => GitCommitDialog.Open(project)));
                }

                // The whole tree's diff, in the same pager one file's opens in. Tracked as
                // the viewer, so the next row clicked replaces it.
                if (repo.Changed > 0)
                    opts.Add(new FloatMenuOption("Diff all", () => DiffAll(project)));
            }

            FilesView.AddFileActions(opts, project, repo.Dir, project);

            opts.Add(new FloatMenuOption("Terminal (host)", () =>
                SessionHub.Instance.SessionStore.RunHostShell(project,
                    session => TerminalWindow.Open(session), UiLayout.Fail)));

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
            FilesView.AddOpenIn(opts, abs);

            bool canStage = Any(node, NeedsStage);
            bool canUnstage = Any(node, IsStaged);
            if (canStage)
                opts.Add(new FloatMenuOption("Stage", () => Stage(repo, node.Rel)));
            if (canUnstage)
                opts.Add(new FloatMenuOption("Unstage", () => Unstage(repo, node.Rel)));

            if (!node.IsDir && Present(node.Status))
                opts.Add(new FloatMenuOption("Edit", () =>
                    FilesView.EditFile(project, abs, "edit-" + node.Name)));

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

        static void Copy(string text) => DaemonClipboard.Copy(text,
            () => Messages.Message($"SlopWorld: copied {text}", MessageTypeDefOf.SilentInput,
                false), UiLayout.Fail);

        // ------------------------------------------------------------------ the diff
        //
        // Diffs run in a host pager session using the project's working directory.

        // Files delegates diff creation here; both trees expose the same reader headers.
        static int _diffRequest;
        static string _pendingDiffProject, _pendingDiffRel;
        static bool _pinPendingDiff;

        internal static void CancelPendingDiff()
        {
            ++_diffRequest;
            _pendingDiffProject = _pendingDiffRel = null;
            _pinPendingDiff = false;
        }

        public static void OpenDiff(string project, string abs, string label) =>
            OpenDiff(project, abs, label, false);

        static void OpenDiff(string project, string abs, string label, bool directory)
        {
            CancelPendingDiff();
            int request = _diffRequest;
            _pendingDiffProject = project;
            var known = Known(project);
            _pendingDiffRel = known == null ? null : RelOf(known, abs);
            Fetch(project, () =>
            {
                if (request != _diffRequest) return;
                bool pin = _pinPendingDiff;
                _pendingDiffProject = _pendingDiffRel = null;
                _pinPendingDiff = false;
                var repo = Known(project);
                string rel = repo == null ? null : RelOf(repo, abs);
                string status = null;
                bool changed = rel != null && (directory
                    ? HasChangesUnder(repo, rel)
                    : repo.Changes.TryGetValue(rel, out status));
                if (!changed)
                {
                    if (repo != null && !repo.Truncated && rel != null)
                        Viewers.Invalidate(tab => tab.Owns(project, DiffKey(rel)));
                    UiLayout.Fail($"nothing to diff in {System.IO.Path.GetFileName(abs)}");
                    return;
                }
                if (directory) ClearSelection();
                else Tree.SelectKey(ContentTreeView.SelectionKey(project, rel));
                AgentSidebar.RememberGit(project, rel);
                Viewers.OpenFresh(project, DiffCmd(repo, rel, status), label, DiffKey(rel));
                if (pin) Viewers.LockPreview(project, DiffKey(rel));
            });
        }

        static void Diff(Node node, Repo repo) =>
            OpenDiff(repo.Project, Abs(repo, node), "diff-" + node.Name, node.IsDir);

        static bool HasChangesUnder(Repo repo, string rel)
        {
            foreach (var path in repo.Changes.Keys)
                if (path.StartsWith(rel.TrimEnd('/') + "/", System.StringComparison.Ordinal))
                    return true;
            return false;
        }

        // Use delta as git's pager because daemon errands are argv, not shell pipelines. Force
        // color and LESS=R: git's default X avoids the alternate screen, preventing the pane
        // from sending wheel input to less; F would quit on short diffs. `-C` anchors paths
        // when a project points below the repository root.
        static string DiffCmd(Repo repo, string rel, string status)
        {
            string git = "git -C " + Pager.Quote(repo.Root) +
                " -c " + Pager.Quote("core.pager=LESS=R delta --paging=always") + " --paginate";

            // A truly untracked file has no index entry or HEAD blob to diff against.
            // `--no-index` against the empty file shows its current contents as added. An
            // unstaged add (` A`) can still be diffed against HEAD; sending it through
            // `--no-index` makes the pager finish without leaving the diff visible.
            if (status == "??")
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
            CancelPendingDiff();
            ClearSelection();
            Viewers.ReleasePreview();
        }

        public static void CloseViewerIf(string session) => Viewers.CloseIf(session);

        public static bool CloseViewerTab(string session)
        {
            if (Viewers.CloseTab(session)) return true;

            // PagerTabs is UI-lifetime state. A game restart loses the owner of an ephemeral
            // diff, but the daemon still owns the routed session and can stop it directly.
            var info = SessionHub.Instance.Get(session);
            if (info == null || !info.Ephemeral) return false;
            if ((RowActions.Of(info) & RowAct.Diff) == 0) return false;
            SessionHub.Instance.SessionStore.Stop(session);
            return true;
        }

        static void ClearSelection()
        {
            Tree.ClearSelection();
        }
    }
}
