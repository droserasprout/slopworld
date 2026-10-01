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
            (s, j) => s.Command = j.Launch.Command,
            (s, j) => s.CommandPreset = j.Launch.CommandPreset,
            (s, j) => s.Cmd = !j.Launch.HasCmd ? "" : j.Launch.Cmd,
            (s, j) => s.Args = !j.Launch.HasArgs ? "" : j.Launch.Args,
            (s, j) => s.Sandbox = j.Launch.Sandbox.ToList(),
            (s, j) => s.PersistentTmp = j.Launch.PersistentTmp,
            (s, j) => s.Agent = j.Launch.Agent,
            (s, j) => s.State = ParseState(j.Runtime.State),
            (s, j) => s.Alive = j.Runtime.Alive,
            (s, j) => s.Network = NetworkModeText.Parse(j.Launch.Network),
            (s, j) => s.Dns = DnsConfig.FromWire(j.Launch.Dns),
            (s, j) => s.Limits = SessionLimits.FromWire(j.Launch.Limits),
            (s, j) => s.Mounts = MountEntry.ListFromWire(j.Launch.Mounts),
            (s, j) => s.Autostart = j.Launch.Autostart,
            (s, j) => s.AutoResume = j.Launch.AutoResume,
            (s, j) => s.AutoResumePending = j.Runtime.AutoResumePending,
            (s, j) => s.Worker = j.Worker.Enabled,
            (s, j) => s.Parent = j.Worker.Parent,
            (s, j) => s.TaskId = j.Worker.TaskId,
            (s, j) => s.Durable = j.Worker.Durable,
            (s, j) => s.Ephemeral = j.Ephemeral,
            (s, j) => s.Host = j.Host,
            (s, j) => s.ProcessRunning = j.Runtime.ProcessRunning,
            (s, j) => s.Cols = (int)j.Runtime.Cols,
            (s, j) => s.Rows = (int)j.Runtime.Rows,
            (s, j) => s.Title = j.Runtime.Title,
            (s, j) => s.Label = j.Label,
            (s, j) => s.Intent = j.Intent,
            (s, j) => s.ReaderPath = j.Reader.Path,
            (s, j) => s.ReaderKey = j.Reader.Key,
            (s, j) => s.ReaderScope = j.Reader.Scope,
            (s, j) => s.ReaderPinned = j.Reader.Pinned,
            (s, j) => s.ReaderLine = j.Reader.Line,
            (s, j) => s.Bell = j.Runtime.Bell,
            (s, j) => s.LastChange = (long)j.Runtime.LastChange,
            (s, j) => s.StateSince = (long)j.Runtime.StateSince,
            (s, j) => s.RunId = (long)j.Runtime.RunId,
        };

        public static SessionInfo FromWire(Wire.SessionView j)
        {
            var session = new SessionInfo();
            foreach (var read in WireFields) read(session, j);
            return session;
        }

    }
}
