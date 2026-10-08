using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class WorkerWorktreeOptionsTests
    {
        public static void ExistingWorktreeClearsStaleCreationFields()
        {
            var options = new WorkerWorktreeOptions
            {
                Worktree = "existing",
                BaseRevision = "old-base",
                WorktreeName = "old-name",
            };
            var request = new Wire.SpawnWorkerReq { Base = "stale", WorktreeName = "stale" };
            options.ApplyTo(request);
            Assert.That(request.Worktree, Is.EqualTo("existing"));
            Assert.That(request.NewWorktree, Is.False);
            Assert.That(request.Base, Is.Empty);
            Assert.That(request.WorktreeName, Is.Empty);
            options.NewWorktree = true;
            options.ApplyTo(request);
            Assert.That(request.NewWorktree, Is.True);
            Assert.That(request.Base, Is.EqualTo("old-base"));
            Assert.That(request.WorktreeName, Is.EqualTo("old-name"));
        }
    }
}
