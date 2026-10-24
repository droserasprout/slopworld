using System.Linq;

namespace SlopWorld
{
    public partial class SessionInfo
    {
        public static SessionInfo FromWire(Wire.SessionView j) =>
            new SessionInfo
            {
                Name = j.Name,
                Project = j.Project,
                Worktree = j.Worktree,
                WorktreeName = j.WorktreeName,
                Dir = j.Dir,
                Command = j.Launch.Command,
                CommandPreset = j.Launch.CommandPreset,
                Cmd = !j.Launch.HasCmd ? "" : j.Launch.Cmd,
                Args = !j.Launch.HasArgs ? "" : j.Launch.Args,
                Sandbox = j.Launch.Sandbox.ToList(),
                PersistentTmp = j.Launch.PersistentTmp,
                Agent = j.Launch.Agent,
                State = ParseState(j.Runtime.State),
                Alive = j.Runtime.Alive,
                Network = NetworkModeText.Parse(j.Launch.Network),
                Dns = DnsConfig.FromWire(j.Launch.Dns),
                Limits = SessionLimits.FromWire(j.Launch.Limits),
                Mounts = MountEntry.ListFromWire(j.Launch.Mounts),
                Autostart = j.Launch.Autostart,
                AutoResume = j.Launch.AutoResume,
                AutoResumePending = j.Runtime.AutoResumePending,
                Worker = j.Worker.Enabled,
                Parent = j.Worker.Parent,
                TaskId = j.Worker.TaskId,
                Durable = j.Worker.Durable,
                Ephemeral = j.Ephemeral,
                Host = j.Host,
                ProcessRunning = j.Runtime.ProcessRunning,
                Cols = (int)j.Runtime.Cols,
                Rows = (int)j.Runtime.Rows,
                Title = j.Runtime.Title,
                Label = j.Label,
                Intent = j.Intent,
                ReaderPath = j.Reader.Path,
                ReaderKey = j.Reader.Key,
                ReaderScope = j.Reader.Scope,
                ReaderPinned = j.Reader.Pinned,
                ReaderLine = j.Reader.Line,
                Bell = j.Runtime.Bell,
                LastChange = (long)j.Runtime.LastChange,
                StateSince = (long)j.Runtime.StateSince,
                RunId = (long)j.Runtime.RunId,
            };
    }
}
