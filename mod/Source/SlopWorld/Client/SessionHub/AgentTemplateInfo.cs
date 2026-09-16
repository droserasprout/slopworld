using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // A personal template's catalog projection. The daemon sends snapshots for creation, while
    // the picker only needs the portable defaults to seed the existing agent form controls.
    public class AgentTemplateInfo
    {
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
        public List<string> Prompts = new List<string>();
        public bool SlopworldMd;
        public bool InstructionsBreadcrumb = true;
        public bool PersistentTmp;
        public bool BreadcrumbYolo = true;
        public NetworkMode Network = NetworkMode.Private;
        public DnsConfig Dns = DnsConfig.Resolved();
        public SessionLimits Limits;
        public bool Autostart;
        public bool AutoResume;
        // Keep the daemon's complete snapshots alongside the friendly projection. Editing a
        // name or description must not discard captured preset/prompt definitions that are no
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
                Name = j["name"].AsString(),
                Version = j["version"].AsLong(0),
                Description = j["description"].AsString(),
                Source = origin["source"].AsString("personal"),
                OriginProject = origin["project"].AsString(),
                OriginAgent = origin["agent"].AsString(),
                Command = defaults["command"]["name"].AsString(),
                Cmd = defaults["cmd"].IsNull ? "" : defaults["cmd"].AsString(),
                Sandbox = Strings(defaults["sandbox"]),
                Prompts = defaults["prompts"].Items.Select(p => p["name"].AsString()).ToList(),
                SlopworldMd = defaults["slopworld_md"].AsBool(false),
                InstructionsBreadcrumb = defaults["instructions_breadcrumb"].AsBool(true),
                PersistentTmp = defaults["persistent_tmp"].AsBool(false),
                BreadcrumbYolo = defaults["breadcrumb_yolo"].AsBool(true),
                Network = NetworkModeText.Parse(defaults["network"].AsString(WireProtocol.NetworkMode.Private)),
                Dns = DnsConfig.FromJson(defaults["dns"]),
                Limits = SessionLimits.FromJson(defaults["limits"]),
                Autostart = defaults["autostart"].AsBool(false),
                AutoResume = defaults["auto_resume"].AsBool(false),
                DefaultsJson = JVal.ToJson(defaults),
                OriginJson = JVal.ToJson(origin),
            };
        }

        public AgentTemplateInfo Copy() => new AgentTemplateInfo
        {
            Name = Name,
            Version = Version,
            Description = Description,
            Source = Source,
            OriginProject = OriginProject,
            OriginAgent = OriginAgent,
            Command = Command,
            Cmd = Cmd,
            Sandbox = new List<string>(Sandbox),
            Prompts = new List<string>(Prompts),
            SlopworldMd = SlopworldMd,
            InstructionsBreadcrumb = InstructionsBreadcrumb,
            PersistentTmp = PersistentTmp,
            BreadcrumbYolo = BreadcrumbYolo,
            Network = Network,
            Dns = Dns?.Copy() ?? DnsConfig.Resolved(),
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

        public List<LibraryItemInfo> BreadcrumbCatalog() =>
            JVal.Parse(DefaultsJson)["prompts"].Items.Select(prompt => new LibraryItemInfo
            {
                Name = prompt["name"].AsString(),
                Text = prompt["text"].AsString(),
                Kind = LibraryItemKind.Breadcrumb,
            }).Concat(SessionHub.Instance.Catalog.Library).GroupBy(p => p.Name).Select(g => g.First()).ToList();

        // Apply the portable editor form to the definition while preserving definitions that
        // came from a source catalog but are no longer installed locally.
        public string ToJson(SessionInfo form)
        {
            var old = JVal.Parse(DefaultsJson ?? "{}");
            string command = CommandJson(old, form);
            var dependencies = JVal.Parse(command)["sandbox"].Items.Select(item => item.AsString());
            string snapshots = SandboxSnapshots(old, form.Sandbox.Concat(dependencies));
            var sandbox = JVal.Parse(snapshots).Items.Select(item => item["name"].AsString());
            string prompts = PromptSnapshots(old, form.Breadcrumbs);
            string patch = "{" +
                $"\"command\":{command}," +
                $"\"cmd\":{(string.IsNullOrWhiteSpace(form.Cmd) ? "null" : JVal.Q(form.Cmd))}," +
                $"\"sandbox\":{Strings(sandbox)},\"sandbox_presets\":{snapshots}," +
                $"\"prompts\":{prompts}," +
                $"\"slopworld_md\":{JVal.B(form.SlopworldMd)}," +
                $"\"instructions_breadcrumb\":{JVal.B(form.InstructionsBreadcrumb)}," +
                $"\"persistent_tmp\":{JVal.B(form.PersistentTmp)}," +
                $"\"breadcrumb_yolo\":{JVal.B(form.BreadcrumbYolo)}," +
                $"\"network\":{JVal.Q(NetworkModeText.Name(form.NetworkOverride ?? form.Network))}," +
                $"\"dns\":{(form.DnsOverride ?? form.Dns ?? DnsConfig.Resolved()).ToJson()}," +
                $"\"limits\":{form.Limits.ToJson()}," +
                $"\"autostart\":{JVal.B(form.Autostart)}," +
                $"\"auto_resume\":{JVal.B(form.AutoResume)}" +
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
            s.Breadcrumbs = new List<string>(Prompts);
            s.SlopworldMd = SlopworldMd;
            s.InstructionsBreadcrumb = InstructionsBreadcrumb;
            s.PersistentTmp = PersistentTmp;
            s.BreadcrumbYolo = BreadcrumbYolo;
            s.Network = Network;
            s.NetworkOverride = Network;
            s.Dns = Dns?.Copy() ?? DnsConfig.Resolved();
            s.DnsOverride = Dns?.Copy() ?? DnsConfig.Resolved();
            s.Limits = Limits;
            s.EffectiveLimits = Limits;
            s.Autostart = Autostart;
            s.AutoResume = AutoResume;
            s.Mounts = new List<MountEntry>();
        }

        static List<string> Strings(JVal array) =>
            array.Items.Select(i => i.AsString()).ToList();

        static string Strings(IEnumerable<string> values) =>
            "[" + string.Join(",", values.Select(JVal.Q).ToArray()) + "]";

        static string CommandJson(JVal old, SessionInfo form)
        {
            string name = string.IsNullOrEmpty(form.CommandPreset) ? form.Command : form.CommandPreset;
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

        static string PromptSnapshots(JVal old, IEnumerable<string> selected) =>
            Snapshots(old["prompts"], selected, name =>
            {
                var item = SessionHub.Instance.Catalog.Library.FirstOrDefault(i => i.Name == name);
                return item == null ? null : "{\"name\":" + JVal.Q(item.Name) +
                    ",\"text\":" + JVal.Q(item.Text ?? "") + "}";
            });

        // Resolve in selection order: prompt order is also delivery order. Captured values
        // win over live catalogs until the player selects a different entry.
        static string Snapshots(JVal captured, IEnumerable<string> selected, Func<string, string> lookup)
        {
            var output = new List<string>();
            foreach (var name in (selected ?? Enumerable.Empty<string>()).Distinct())
            {
                var snapshot = captured.Items.FirstOrDefault(item => item["name"].AsString() == name);
                string value = snapshot == null ? lookup(name) : JVal.ToJson(snapshot);
                if (value == null) throw new InvalidOperationException("Unknown template entry: " + name);
                output.Add(value);
            }
            return "[" + string.Join(",", output.ToArray()) + "]";
        }
    }
}
