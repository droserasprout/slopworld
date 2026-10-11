using System;

namespace SlopWorld
{
    // Shared root-only worktree transport. Consumers own filtering, snapshots and retries.
    internal static class WorktreeRequests
    {
        internal static void Load(string project, Action<Wire.WorktreesReply> loaded,
            Action<string> fail, int timeout = 5000) =>
            DaemonClient.Get<Wire.WorktreesReply>(WireProtocol.Routes.Worktrees + "?project=" + Uri.EscapeDataString(project),
                loaded, fail, TaskInfo.Host, timeout);
    }
}
