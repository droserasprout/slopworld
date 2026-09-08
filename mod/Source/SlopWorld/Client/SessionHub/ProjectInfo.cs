using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    public class ProjectInfo
    {
        public string Name = "";
        public string Dir = "";
        // The daemon coins TempRoot/name and makes it when the first agent starts there. It
        // is /tmp that is temporary, not the entry.
        public bool Temp;
        // Sandbox presets by name. Every agent runs in a sandbox; this says what it reaches.
        public List<string> Sandbox = new List<string>();
        public NetworkMode Network = NetworkMode.Private;
        public List<string> Breadcrumbs = new List<string>();
        // A project-level DNS choice is the default for its agents. Resolved is the default.
        public DnsConfig Dns = DnsConfig.Resolved();

        // The daemon coins the path and is the only thing that writes it; this is so the dialog
        // can show what a name is about to become before anything is saved.
        public const string TempRoot = WireContract.TempRoot;

        // The same rule as the daemon's `slug`.
        public static string TempDir(string name)
        {
            var slug = new System.Text.StringBuilder();
            foreach (char c in (name ?? "").Trim())
            {
                if (char.IsWhiteSpace(c) || c == ':' || c == '.' || c == '/')
                {
                    if (slug.Length > 0 && slug[slug.Length - 1] != '-') slug.Append('-');
                }
                else slug.Append(c);
            }
            return TempRoot + "/" + slug.ToString().Trim('-');
        }

        public static ProjectInfo FromJson(JVal j) => new ProjectInfo
        {
            Name = j["name"].AsString(),
            Dir = j["dir"].AsString(),
            Temp = j["temp"].AsBool(false),
            Sandbox = Strings(j["sandbox"]),
            Breadcrumbs = Strings(j["breadcrumbs"]),
            Network = NetworkModeText.Parse(j["network"].AsString(WireContract.NetworkMode.Private)),
            Dns = DnsConfig.FromJson(j["dns"]),
        };

        public string ToJson() =>
            "{" +
            $"\"name\":{JVal.Q(Name)},\"dir\":{JVal.Q(Dir)},\"temp\":{JVal.B(Temp)}," +
            $"\"sandbox\":{Arr(Sandbox)}," +
            $"\"breadcrumbs\":{Arr(Breadcrumbs)},\"network\":{JVal.Q(NetworkModeText.Name(Network))}," +
            $"\"dns\":{Dns.ToJson()}}}";

        public ProjectInfo Copy() => new ProjectInfo
        {
            Name = Name,
            Dir = Dir,
            Temp = Temp,
            Sandbox = new List<string>(Sandbox),
            Breadcrumbs = new List<string>(Breadcrumbs),
            Network = Network,
            Dns = Dns.Copy(),
        };

        static List<string> Strings(JVal a) => a.Items.Select(i => i.AsString()).ToList();

        static string Arr(List<string> items) =>
            "[" + string.Join(",", items.Select(JVal.Q).ToArray()) + "]";
    }
}
