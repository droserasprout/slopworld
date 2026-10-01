namespace SlopWorld
{
    // Launch intent; only a new worktree accepts a base revision and requested name.
    public sealed class WorkerWorktreeOptions
    {
        public string Worktree = "";
        public bool NewWorktree;
        public string BaseRevision = "";
        public string WorktreeName = "";

        internal void ApplyTo(Wire.SpawnWorkerReq request)
        {
            request.Worktree = Worktree ?? "";
            request.NewWorktree = NewWorktree;
            request.Base = NewWorktree ? BaseRevision ?? "" : "";
            request.WorktreeName = NewWorktree ? WorktreeName ?? "" : "";
        }
    }
}
