using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public partial class SessionInfo
    {
        // Keep wire names in one table. A new daemon field is added as one mapping entry
        // instead of making the object initializer grow another protocol-shaped block.
        static readonly Action<SessionInfo, Wire.SessionView>[] WireFields =
        {
            (s, j) => s.Name = j.Name,
            (s, j) => s.Project = j.Project,
            (s, j) => s.Worktree = j.Worktree,
            (s, j) => s.WorktreeName = j.WorktreeName,
            (s, j) => s.Dir = j.Dir,
            (s, j) => s.Command = j.Command,
            (s, j) => s.CommandPreset = j.CommandPreset,
            (s, j) => s.Cmd = !j.HasCmd ? "" : j.Cmd,
            (s, j) => s.Sandbox = j.Sandbox.ToList(),
            (s, j) => s.PersistentTmp = j.PersistentTmp,
            (s, j) => s.Agent = j.Agent,
            (s, j) => s.State = ParseState(j.State),
            (s, j) => s.Alive = j.Alive,
            (s, j) => s.Network = NetworkModeText.Parse(j.Network),
            (s, j) => s.Dns = DnsConfig.FromWire(j.Dns),
            (s, j) => s.Limits = SessionLimits.FromWire(j.Limits),
            (s, j) => s.Mounts = MountEntry.ListFromWire(j.Mounts),
            (s, j) => s.Autostart = j.Autostart,
            (s, j) => s.AutoResume = j.AutoResume,
            (s, j) => s.AutoResumePending = j.AutoResumePending,
            (s, j) => s.Worker = j.Worker,
            (s, j) => s.Parent = j.Parent,
            (s, j) => s.TaskId = j.TaskId,
            (s, j) => s.Durable = j.Durable,
            (s, j) => s.Ephemeral = j.Ephemeral,
            (s, j) => s.Host = j.Host,
            (s, j) => s.ProcessRunning = j.ProcessRunning,
            (s, j) => s.Cols = (int)j.Cols,
            (s, j) => s.Rows = (int)j.Rows,
            (s, j) => s.Title = j.Title,
            (s, j) => s.Label = j.Label,
            (s, j) => s.Bell = j.Bell,
            (s, j) => s.LastChange = (long)j.LastChange,
            (s, j) => s.StateSince = (long)j.StateSince,
            (s, j) => s.RunId = (long)j.RunId,
        };

        public static SessionInfo FromWire(Wire.SessionView j)
        {
            var session = new SessionInfo();
            foreach (var read in WireFields) read(session, j);
            return session;
        }

    }
}
