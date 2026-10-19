using RimWorld;
using Verse;
using Node = SlopWorld.GitStore.Node;
using Repo = SlopWorld.GitStore.Repo;

namespace SlopWorld
{
    // Coordinates Git mutations and refreshes. GitCommands owns command construction;
    // GitStore owns snapshot identity and rename paths, while Rendering owns drawing.
    public static partial class GitView
    {
        static bool Any(Node node, System.Func<GitStatus, bool> test)
        {
            if (!node.IsDir)
                return test(node.Status);
            if (node.Kids == null)
                return false;
            foreach (var child in node.Kids)
                if (Any(child, test))
                    return true;
            return false;
        }

        static bool HasStaged(Repo repo)
        {
            foreach (var status in repo.Changes.Values)
                if (new GitStatus(status).IsStaged)
                    return true;
            return false;
        }

        static void GitAction(
            Repo repo,
            string command,
            string notice,
            bool completeCommand = false,
            System.Action ok = null,
            System.Action<string> fail = null
        )
        {
            if (
                repo == null
                || !repo.IsRepo
                || repo.Error != null
                || string.IsNullOrEmpty(repo.Root)
            )
            {
                (fail ?? UiLayout.Fail)("repository is not available");
                return;
            }

            // A project may point below the repository root. Validate its selected checkout
            // directory while Git uses the repository root returned by the daemon.
            string full = completeCommand
                ? command
                : "git -C " + Pager.Quote(repo.Root) + " " + command;
            DaemonClient.Post<Wire.OutputResult>(
                WireProtocol.Routes.FileAction,
                FilesView.ScopeAction(repo.Project, repo.Dir, full),
                _ =>
                {
                    Messages.Message("SlopWorld: " + notice, MessageTypeDefOf.SilentInput, false);
                    Fetch(repo.Project);
                    ok?.Invoke();
                },
                msg => (fail ?? UiLayout.Fail)("Git: " + msg)
            );
        }

        static void Stage(Repo repo, string rel) =>
            GitAction(repo, GitCommands.Stage(repo.Root, rel), "staged " + rel, true);

        static void Unstage(Repo repo, Node node) =>
            GitAction(
                repo,
                GitCommands.Unstage(repo.Root, GitStore.UnstagePaths(node)),
                "unstaged " + node.Rel,
                true
            );

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

        public static void Commit(
            string project,
            string message,
            System.Action ok,
            System.Action<string> fail
        )
        {
            var repo = Known(project);
            if (repo == null || !HasStaged(repo))
            {
                fail("no staged changes to commit");
                return;
            }

            GitAction(
                repo,
                "commit -m " + Pager.Quote(message) + " --",
                "committed changes",
                ok: ok,
                fail: fail
            );
        }
    }
}
