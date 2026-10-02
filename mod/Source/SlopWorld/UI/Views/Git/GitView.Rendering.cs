using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static partial class GitView
    {
        // The state line and then the tree, or the state line alone where there is no tree to
        // draw. One row either way, which is what Measure counts.
        static float Body(float width, float y, Repo repo)
        {
            if (repo.Loading && repo.Tree == null)
                return ViewChrome.Note(width, y, 0, "Loading Git status", UiTheme.Faint);
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
            // Add one pixel on each side of the measured width.
            // Some fonts have glyph ink that extends beyond the reported advance.
            // Without the added pixels, the rect can clip the last digit.
            float w = UiTheme.Wide(text) + 2f;
            GUI.color = color;
            UiText.RowLabel(new Rect(right - w, y, w, UiTheme.TinyRowH), text,
                TextAnchor.MiddleRight);
            return right - w - 5f;
        }

        static bool Any(Node node, System.Func<GitStatus, bool> test)
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
                if (new GitStatus(status).IsStaged) return true;
            return false;
        }

        static void GitAction(Repo repo, string command, string notice, bool completeCommand = false)
        {
            if (repo == null || !repo.IsRepo || repo.Error != null || string.IsNullOrEmpty(repo.Root))
            {
                UiLayout.Fail("repository is not available");
                return;
            }

            // A project may point below the repository root. Validate its selected checkout
            // directory while Git uses the repository root returned by the daemon.
            string full = completeCommand ? command : "git -C " + Pager.Quote(repo.Root) + " " + command;
            DaemonClient.Post<Wire.OutputResult>(WireProtocol.Routes.FileAction, FilesView.ScopeAction(repo.Project, repo.Dir, full),
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
            GitAction(repo, GitCommands.Unstage(repo.Root, rel), "unstaged " + rel, true);

        static void StageAll(Repo repo) => GitAction(repo, "add --all", "staged all changes");

        static void UnstageAll(Repo repo) =>
            GitAction(repo, GitCommands.Unstage(repo.Root, "."), "unstaged all changes", true);

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
                if (node.Owner.CountsComplete && !node.Owner.Truncated)
                    right = Tail(right, row.y, "bin", UiTheme.Faint);
            }
            else
            {
                if (node.Deleted > 0) right = Tail(right, row.y, "-" + node.Deleted,
                    UiTheme.Bad);
                if (node.Added > 0) right = Tail(right, row.y, "+" + node.Added,
                    UiTheme.Yes);
            }
            return Tail(right, row.y, node.Status.Mark, MarkColor(node.Status));
        }

        // Every changed file offers Diff. Existing files additionally offer Edit; text and
        // supported image files offer View.
        static RowAct Acts(Node node)
        {
            if (node.IsDir) return RowAct.None;

            var acts = RowAct.Diff;
            if (!node.Status.Present) return acts;

            acts |= RowAct.Edit;
            if (FilesView.IsText(node.Name) || FilesView.IsImage(node.Name)) acts |= RowAct.View;
            return acts;
        }

        // Staged is the color of a thing that is going somewhere. Everything else is the
        // color of a thing that is not. Untracked is neither, and is dimmer than both.
        static Color MarkColor(GitStatus status)
        {
            if (status.Unknown || status.Untracked) return UiTheme.Faint;
            if (status.Unmerged) return UiTheme.Bad;
            return status.IsStaged ? UiTheme.Yes : UiTheme.Warn;
        }

        static string Says(Node node)
        {
            var status = node.Status;
            string s = status.Pair;
            if (status.Untracked) return "untracked";
            if (status.Unmerged) return "unmerged";

            char staged = status.Staged, worktree = status.Worktree;
            var parts = new List<string>();
            if (staged != ' ') parts.Add(Word(staged) + ", staged");
            if (worktree != ' ') parts.Add(Word(worktree) + " since");
            return parts.Count == 0 ? s : string.Join("; ", parts.ToArray());
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
        // Folding the change tree only changes navigation. Keep the active diff visible while
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
            project = SidebarScopes.Key(project);
            var repo = Known(project);
            if (repo == null || string.IsNullOrEmpty(rel) || !repo.Changes.ContainsKey(rel))
                return false;
            TreeController.SetGroupCollapsed(project, false);
            TreeController.SetGroupCollapsed(SidebarScopes.Find(project)?.ProjectKey, false);
            Tree.RevealKey(ContentTreeView.SelectionKey(project, rel));
            return true;
        }

        // Row selection asks for current content. Routed headers only restore focus.
        static void Open(Node node, Repo repo)
        {
            Tree.Select(node);
            Diff(node, repo);
        }

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

        // FilesView queries only the repository cache. These lookups never fetch.

        static Repo Known(string project)
        {
            if (string.IsNullOrEmpty(project)) return null;
            project = SidebarScopes.Key(project);
            var dir = SidebarScopes.Directory(project);
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
            project = SidebarScopes.Key(project);
            CancelPendingDiff();
            int request = _diffRequest;
            void OpenCurrent()
            {
                if (request != _diffRequest) return;
                var repo = Known(project);
                if (repo == null || repo.Changed <= 0)
                {
                    UiLayout.Fail("nothing to diff");
                    return;
                }
                ClearSelection();
                Viewers.ForPreview().Open(project, DiffCmd(repo, null, null), FilesView.ReaderLabel(project, "diff"),
                    null, "", "diff");
            }

            // The visible tree already has a status snapshot. Start its pager now; Git reads
            // the actual diff when the command runs. A cold cache still needs the root first.
            var known = Known(project);
            if (known != null && !string.IsNullOrEmpty(known.Root)) OpenCurrent();
            else Fetch(project, OpenCurrent);
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
                    opts.Add(new FloatMenuOption("Commit staged changes",
                        () => GitCommitDialog.Open(project)));
                }

                // The whole tree's diff, in the same pager one file's opens in. Tracked as
                // the viewer, so the next row clicked replaces it.
                if (repo.Changed > 0)
                    opts.Add(new FloatMenuOption("Diff all", () => DiffAll(project)));
            }

            FilesView.AddFileActions(opts, project, repo.Dir, SidebarScopes.Label(project));

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
            FilesView.AddOpenIn(opts, abs, project);

            bool canStage = Any(node, status => status.NeedsStage);
            bool canUnstage = Any(node, status => status.IsStaged);
            if (canStage)
                opts.Add(new FloatMenuOption("Stage", () => Stage(repo, node.Rel)));
            if (canUnstage)
                opts.Add(new FloatMenuOption("Unstage", () => Unstage(repo, node.Rel)));

            if (!node.IsDir && node.Status.Present)
                opts.Add(new FloatMenuOption("Edit", () =>
                    FilesView.EditFile(project, abs, "edit-" + node.Name)));

            opts.Add(new FloatMenuOption("Diff", () =>
            {
                // A directory diff includes every change under it, so the view marks no row.
                // A file diff applies to the row itself.
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

        // Files delegates diff creation here. Both trees expose the same reader headers.
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
            project = SidebarScopes.Key(project);
            CancelPendingDiff();
            int request = _diffRequest;
            _pendingDiffProject = project;
            var known = Known(project);
            _pendingDiffRel = known == null ? null : RelOf(known, abs);
            void OpenCurrent()
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
                Viewers.OpenFresh(project, DiffCmd(repo, rel, status), FilesView.ReaderLabel(project, label), DiffKey(rel));
                if (pin) Viewers.LockPreview(project, DiffKey(rel));
            }

            // The row came from this snapshot. Avoid a second status HTTP round trip before
            // starting its pager; only a cold cache needs to discover the repository root.
            if (known != null && !string.IsNullOrEmpty(known.Root)) OpenCurrent();
            else Fetch(project, OpenCurrent);
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

        static string DiffCmd(Repo repo, string rel, string status) =>
            GitCommands.Diff(repo.Root, rel, new GitStatus(status).Untracked);

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
