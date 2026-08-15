using System.Linq;

namespace SlopWorld
{
    public partial class SessionInfo
    {
        public static SessionInfo FromJson(JVal j) => new SessionInfo
        {
            Name = j["name"].AsString(),
            Project = j["project"].AsString(),
            Dir = j["dir"].AsString(),
            Command = j["command"].AsString(),
            CommandPreset = j["command_preset"].AsString(),
            Cmd = j["cmd"].IsNull ? "" : j["cmd"].AsString(),
            Sandbox = j["sandbox"].Items.Select(i => i.AsString()).ToList(),
            Agent = j["agent"].AsString(),
            State = ParseState(j["state"].AsString()),
            Alive = j["alive"].AsBool(),
            Network = NetworkModeText.Parse(j["network"].AsString("private")),
            NetworkOverride = j["network_override"].IsNull
                ? (NetworkMode?)null
                : NetworkModeText.Parse(j["network_override"].AsString()),
            Dns = DnsConfig.FromJson(j["dns"]),
            DnsOverride = j["dns_override"].IsNull ? null : DnsConfig.FromJson(j["dns_override"]),
            Limits = SessionLimits.FromJson(j["limits_override"]),
            EffectiveLimits = SessionLimits.FromJson(j["limits"]),
            Autostart = j["autostart"].AsBool(false),
            BreadcrumbYolo = j["breadcrumb_yolo"].AsBool(true),
            Breadcrumbs = j["breadcrumbs"].Items.Select(i => i.AsString()).ToList(),
            BreadcrumbsPending = j["breadcrumbs_pending"].AsBool(false),
            Ephemeral = j["ephemeral"].AsBool(false),
            Cols = j["cols"].AsInt(0),
            Rows = j["rows"].AsInt(0),
            Title = j["title"].AsString(),
            Label = j["label"].AsString(),
            Bell = j["bell"].AsBool(false),
            LastChange = j["last_change"].AsLong(0),
            StateSince = j["state_since"].AsLong(0),
        };
    }
}
