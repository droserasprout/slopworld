using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // A personal template's catalog projection. The daemon sends snapshots for creation, while
    // the picker only needs the portable defaults to seed the existing agent form controls.
    public class AgentTemplateInfo
    {
        public static readonly string[] FlagNames = { "persistent_tmp", "autostart", "auto_resume" };
        public HashSet<string> SpecifiedFlags = new HashSet<string>();
        public string Name = "";
        // Versions are daemon-owned compare-and-swap tokens. Zero is reserved for a new
        // definition, which lets the same wire model serve capture, duplicate, and edit.
        public long Version;
        public string Description = "";
        public string Source = "personal";
        public string OriginProject = "";
        public string OriginAgent = "";
        public string Command = "";
        public string Cmd = "";
        public List<string> Sandbox = new List<string>();
        public bool PersistentTmp;
        public NetworkMode Network = NetworkMode.Private;
        public bool NetworkSpecified;
        public DnsConfig Dns = DnsConfig.Resolved();
        public bool DnsSpecified;
        public SessionLimits Limits;
        public bool Autostart;
        public bool AutoResume;
        // Keep the daemon's complete snapshots alongside the friendly projection. Editing a
        // name or description must not discard captured preset definitions that are no
        // longer present in the live catalogs.
        public string DefaultsJson = "{}";
        public string OriginJson = "{}";

        public string DisplayLabel
        {
            get
            {
                string label = string.IsNullOrEmpty(Description) ? Name : Name + "  -  " + Description;
                if (!string.IsNullOrEmpty(OriginProject) && !string.IsNullOrEmpty(OriginAgent))
                    label += "  (from " + OriginProject + "/" + OriginAgent + ")";
                return label;
            }
        }

        public static AgentTemplateInfo FromJson(JVal j)
        {
            var origin = j["origin"];
            var defaults = j["defaults"];
            return new AgentTemplateInfo
            {
                SpecifiedFlags = new HashSet<string>(FlagNames.Where(name => !defaults[name].IsNull)),
                Name = j["name"].AsString(),
                Version = j["version"].AsLong(0),
                Description = j["description"].AsString(),
                Source = origin["source"].AsString("personal"),
                OriginProject = origin["project"].AsString(),
                OriginAgent = origin["agent"].AsString(),
                Command = defaults["command"]["name"].AsString(),
                Cmd = defaults["cmd"].IsNull ? "" : defaults["cmd"].AsString(),
                Sandbox = Strings(defaults["sandbox"]),
                PersistentTmp = defaults["persistent_tmp"].AsBool(false),
                Network = NetworkModeText.Parse(defaults["network"].AsString(WireProtocol.NetworkMode.Private)),
                NetworkSpecified = !defaults["network"].IsNull,
                Dns = DnsConfig.FromJson(defaults["dns"]),
                DnsSpecified = !defaults["dns"].IsNull,
                Limits = SessionLimits.FromJson(defaults["limits"]),
                Autostart = defaults["autostart"].AsBool(false),
                AutoResume = defaults["auto_resume"].AsBool(false),
                DefaultsJson = JVal.ToJson(defaults),
                OriginJson = JVal.ToJson(origin),
            };
        }

        public AgentTemplateInfo Copy() => new AgentTemplateInfo
        {
            SpecifiedFlags = new HashSet<string>(SpecifiedFlags),
            Name = Name,
            Version = Version,
            Description = Description,
            Source = Source,
            OriginProject = OriginProject,
            OriginAgent = OriginAgent,
            Command = Command,
            Cmd = Cmd,
            Sandbox = new List<string>(Sandbox),
            PersistentTmp = PersistentTmp,
            Network = Network,
            NetworkSpecified = NetworkSpecified,
            Dns = Dns?.Copy() ?? DnsConfig.Resolved(),
            DnsSpecified = DnsSpecified,
            Limits = Limits,
            Autostart = Autostart,
            AutoResume = AutoResume,
            DefaultsJson = DefaultsJson,
            OriginJson = OriginJson,
        };

        // Editors and previews share the same snapshot-first catalogs used when saving.
        public CommandInfo ResolveCommand(string name)
        {
            var captured = JVal.Parse(DefaultsJson)["command"];
            return !string.IsNullOrEmpty(name) && captured["name"].AsString() == name
                ? CommandInfo.FromJson(captured) : SessionHub.Instance.Commands.FirstOrDefault(command => command.Name == name);
        }

        public List<PresetInfo> SandboxCatalog() =>
            JVal.Parse(DefaultsJson)["sandbox_presets"].Items.Select(PresetInfo.FromJson)
                .Concat(SessionHub.Instance.Presets).GroupBy(p => p.Name).Select(g => g.First()).ToList();

        // Apply the portable editor form to the definition while preserving definitions that
        // came from a source catalog but are no longer installed locally.
        public string ToJson(SessionInfo form)
        {
            var old = JVal.Parse(DefaultsJson ?? "{}");
            string command = CommandJson(old, form);
            var dependencies = JVal.Parse(command)["sandbox"].Items.Select(item => item.AsString());
            string snapshots = SandboxSnapshots(old, form.Sandbox.Concat(dependencies));
            var sandbox = JVal.Parse(snapshots).Items.Select(item => item["name"].AsString());
            string patch = "{" +
                $"\"command\":{command}," +
                $"\"cmd\":{(string.IsNullOrWhiteSpace(form.Cmd) ? "null" : JVal.Q(form.Cmd))}," +
                $"\"sandbox\":{Strings(sandbox)},\"sandbox_presets\":{snapshots}," +
                $"\"persistent_tmp\":{FlagJson("persistent_tmp", form.PersistentTmp)}," +
                $"\"network\":{(NetworkSpecified ? JVal.Q(NetworkModeText.Name(form.Network)) : "null")}," +
                $"\"dns\":{(DnsSpecified ? form.Dns.ToJson() : "null")}," +
                $"\"limits\":{form.Limits.ToJson()}," +
                $"\"autostart\":{FlagJson("autostart", form.Autostart)}," +
                $"\"auto_resume\":{FlagJson("auto_resume", form.AutoResume)}" +
                "}";
            // This is a full replacement. Deep merging would resurrect cleared limits and
            // optional fields from an old command or DNS definition.
            return "{" + $"\"name\":{JVal.Q(Name)},\"version\":{Version}," +
                $"\"description\":{JVal.Q(Description)},\"origin\":{OriginJson ?? "{}"}," +
                $"\"defaults\":{patch}}}";
        }

        // Seed a new editor, leaving name and project to creation. Mounts and labels are
        // deliberately not template fields; they are contextual or presentation choices.
        public void ApplyTo(SessionInfo s)
        {
            s.Command = Command ?? "";
            s.CommandPreset = Command ?? "";
            s.Cmd = Cmd ?? "";
            s.Sandbox = new List<string>(Sandbox);
            s.PersistentTmp = PersistentTmp;
            s.Network = Network;
            s.Dns = Dns?.Copy() ?? DnsConfig.Resolved();
            s.Limits = Limits;
            s.Autostart = Autostart;
            s.AutoResume = AutoResume;
        }

        string FlagJson(string name, bool value) => SpecifiedFlags.Contains(name) ? JVal.B(value) : "null";

        static List<string> Strings(JVal array) =>
            array.Items.Select(i => i.AsString()).ToList();

        static string Strings(IEnumerable<string> values) =>
            "[" + string.Join(",", values.Select(JVal.Q).ToArray()) + "]";

        static string CommandJson(JVal old, SessionInfo form)
        {
            string name = form.Command;
            if (string.IsNullOrEmpty(name)) return "null";
            if (old["command"]["name"].AsString() == name)
                return JVal.ToJson(old["command"]);
            var command = SessionHub.Instance.Commands.FirstOrDefault(c => c.Name == name);
            if (command == null) throw new InvalidOperationException("Unknown command: " + name);
            return command.ToJson();
        }

        static string SandboxSnapshots(JVal old, IEnumerable<string> selected)
        {
            var names = new HashSet<string>();
            var pending = new Queue<string>(selected);
            var output = new List<string>();
            while (pending.Count > 0)
            {
                string name = pending.Dequeue();
                if (!names.Add(name)) continue;
                var captured = old["sandbox_presets"].Items.FirstOrDefault(p => p["name"].AsString() == name);
                string value = captured == null
                    ? SessionHub.Instance.Presets.FirstOrDefault(p => p.Name == name)?.ToJson()
                    : JVal.ToJson(captured);
                if (value == null) throw new InvalidOperationException("Unknown sandbox preset: " + name);
                output.Add(value);
                foreach (var dependency in JVal.Parse(value)["requires"].Items)
                    pending.Enqueue(dependency.AsString());
            }
            return "[" + string.Join(",", output.ToArray()) + "]";
        }

    }
}
