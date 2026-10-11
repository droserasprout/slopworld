namespace SlopWorld
{
    // Detached success handoff after the worker's session refresh has settled.
    public sealed class WorkerSpawnResult
    {
        public string Session { get; }
        public string Project { get; }
        public string Worktree { get; }

        internal WorkerSpawnResult(string session, SessionInfo info)
        {
            Session = session;
            Project = info?.Project ?? "";
            Worktree = info?.Worktree ?? "";
        }
    }
}
