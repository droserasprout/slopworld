using RimWorld;
using Verse;
using Node = SlopWorld.GitStore.Node;
using Repo = SlopWorld.GitStore.Repo;

namespace SlopWorld
{
    // Sends typed Git mutation requests and refreshes successful operations;
    // GitStore owns snapshot identity and rename paths, while Rendering owns drawing.
    public static partial class GitView
    {
        static bool HasStaged(Repo repo)
        {
            foreach (var status in repo.Changes.Values)
                if (new GitStatus(status.Status).IsStaged)
                    return true;
            return false;
        }

        static void GitAction(
            Repo repo,
            Wire.GitMutationReq request,
            string notice,
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

            request.Project = SidebarScopes.ProjectName(repo.Project) ?? "";
            request.Worktree = SidebarScopes.Worktree(repo.Project);
            request.ExpectedRoot = repo.Root;
            DaemonClient.Post<Wire.Ack>(
                WireProtocol.Routes.Git,
                request,
                _ =>
                {
                    Messages.Message("SlopWorld: " + notice, MessageTypeDefOf.SilentInput, false);
                    Fetch(repo.Project);
                    ok?.Invoke();
                },
                msg => (fail ?? UiLayout.Fail)("Git: " + msg)
            );
        }

        static Wire.GitSelection Selection(Node node)
        {
            var row = new Wire.GitSelection { Path = node.Rel };
            if (!node.IsDir)
                row.Status = node.Status.Pair;
            return row;
        }

        static void Stage(Repo repo, Node node) =>
            GitAction(
                repo,
                new Wire.GitMutationReq { Stage = Selection(node) },
                "staged " + node.Rel
            );

        static void Unstage(Repo repo, Node node) =>
            GitAction(
                repo,
                new Wire.GitMutationReq { Unstage = Selection(node) },
                "unstaged " + node.Rel
            );

        static void StageAll(Repo repo) =>
            GitAction(
                repo,
                new Wire.GitMutationReq { StageAll = new Wire.Empty() },
                "staged all changes"
            );

        static void UnstageAll(Repo repo) =>
            GitAction(
                repo,
                new Wire.GitMutationReq { UnstageAll = new Wire.Empty() },
                "unstaged all changes"
            );

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
                new Wire.GitMutationReq { Commit = message },
                "committed changes",
                ok: ok,
                fail: fail
            );
        }
    }
}
