using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    static partial class SidebarScopes
    {
        // The mod uses the root token; worktree routes also require its host caller identity.
        internal static void Load(string project, Action<List<BrowseScope>, string> done) =>
            DaemonClient.Get<Wire.WorktreesReply>(WireProtocol.Routes.Worktrees + "?project=" + Uri.EscapeDataString(project),
                reply => done(reply.Worktrees.Select(w => new BrowseScope
                {
                    Worktree = w.Id,
                    Name = w.Name,
                    Path = w.Path,
                    Phase = w.Phase,
                    Branch = w.Branch,
                    Error = w.Error
                }).ToList(), null),
                error => done(null, error), TaskInfo.Host);
    }
}
