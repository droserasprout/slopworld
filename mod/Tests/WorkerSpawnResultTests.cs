namespace SlopWorld.Tests
{
    static class WorkerSpawnResultTests
    {
        public static void CapturesDetachedLaunchIdentity()
        {
            var info = new SessionInfo { Project = "project", Worktree = "checkout" };
            var result = new WorkerSpawnResult("worker", info);
            info.Project = "renamed";
            info.Worktree = "moved";
            AssertEx.Equal("worker", result.Session, "launched session");
            AssertEx.Equal("project", result.Project, "detached project");
            AssertEx.Equal("checkout", result.Worktree, "detached worktree");
        }
    }
}
