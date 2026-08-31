using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public partial class SessionInfo
    {
        // Keep wire names in one table. A new daemon field is added as one mapping entry
        // instead of making the object initializer grow another protocol-shaped block.
        static readonly Action<SessionInfo, JVal>[] WireFields =
        {
            (s, j) => s.Name = j["name"].AsString(),
            (s, j) => s.Project = j["project"].AsString(),
            (s, j) => s.Dir = j["dir"].AsString(),
            (s, j) => s.Command = j["command"].AsString(),
            (s, j) => s.CommandPreset = j["command_preset"].AsString(),
            (s, j) => s.Cmd = j["cmd"].IsNull ? "" : j["cmd"].AsString(),
            (s, j) => s.Sandbox = Strings(j["sandbox"]),
            (s, j) => s.SlopworldMd = j["slopworld_md"].AsBool(false),
            (s, j) => s.InstructionsBreadcrumb = j["instructions_breadcrumb"].AsBool(true),
            (s, j) => s.PersistentTmp = j["persistent_tmp"].AsBool(false),
            (s, j) => s.Agent = j["agent"].AsString(),
            (s, j) => s.State = ParseState(j["state"].AsString()),
            (s, j) => s.Alive = j["alive"].AsBool(),
            (s, j) => s.Network = NetworkModeText.Parse(j["network"].AsString("private")),
            (s, j) => s.NetworkOverride = j["network_override"].IsNull
                ? (NetworkMode?)null
                : NetworkModeText.Parse(j["network_override"].AsString()),
            (s, j) => s.Dns = DnsConfig.FromJson(j["dns"]),
            (s, j) => s.DnsOverride = j["dns_override"].IsNull
                ? null : DnsConfig.FromJson(j["dns_override"]),
            (s, j) => s.Limits = SessionLimits.FromJson(j["limits_override"]),
            (s, j) => s.EffectiveLimits = SessionLimits.FromJson(j["limits"]),
            (s, j) => s.Mounts = j["mounts"].IsNull
                ? new List<MountEntry>() : MountEntry.ListFromJson(j["mounts"]),
            (s, j) => s.Autostart = j["autostart"].AsBool(false),
            (s, j) => s.AutoResume = j["auto_resume"].AsBool(false),
            (s, j) => s.AutoResumePending = j["auto_resume_pending"].AsBool(false),
            (s, j) => s.Worker = j["worker"].AsBool(false),
            (s, j) => s.Parent = j["parent"].AsString(),
            (s, j) => s.TaskId = j["task_id"].AsString(),
            (s, j) => s.Durable = j["durable"].AsBool(false),
            (s, j) => s.BreadcrumbYolo = j["breadcrumb_yolo"].AsBool(true),
            (s, j) => s.Breadcrumbs = Strings(j["breadcrumbs"]),
            (s, j) => s.BreadcrumbsPending = j["breadcrumbs_pending"].AsBool(false),
            (s, j) => s.Ephemeral = j["ephemeral"].AsBool(false),
            (s, j) => s.Host = j["host"].AsBool(false),
            (s, j) => s.Cols = j["cols"].AsInt(0),
            (s, j) => s.Rows = j["rows"].AsInt(0),
            (s, j) => s.Title = j["title"].AsString(),
            (s, j) => s.Label = j["label"].AsString(),
            (s, j) => s.Bell = j["bell"].AsBool(false),
            (s, j) => s.LastChange = j["last_change"].AsLong(0),
            (s, j) => s.StateSince = j["state_since"].AsLong(0),
        };

        public static SessionInfo FromJson(JVal j)
        {
            var session = new SessionInfo();
            foreach (var read in WireFields) read(session, j);
            return session;
        }

        static List<string> Strings(JVal array) =>
            array.Items.Select(i => i.AsString()).ToList();
    }
}
