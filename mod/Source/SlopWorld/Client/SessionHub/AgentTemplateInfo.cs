using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // A personal template's catalog projection. The daemon sends snapshots for creation, while
    // the picker only needs the portable defaults to seed the existing agent form controls.
    public class AgentTemplateInfo
    {
        public string Name = "";
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
            };
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
    }
}
