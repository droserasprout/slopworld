using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // A preview is advisory until the daemon accepts the project. Keep the request generation
    // beside the parsed project model so delayed replies can be tested without the game UI.
    public sealed class TempProjectPreviewState
    {
        int _serial;
        public string Name { get; private set; }
        public string Dir { get; private set; }

        public int Begin(string name)
        {
            Name = name ?? "";
            Dir = null;
            return ++_serial;
        }

        public bool Accept(int serial, bool temporary, string name, string dir)
        {
            if (serial != _serial || !temporary || name != Name || string.IsNullOrEmpty(dir))
                return false;
            Dir = dir;
            return true;
        }
    }

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

        // The daemon is the only owner of temporary root policy. This compatibility value is
        // only for the offline connection bootstrap; connected dialogs use metadata.
        public static string TempRoot => SessionHub.Instance.Config?.TemporaryRoot ?? "/tmp/slopworld";

        public static ProjectInfo FromJson(JVal j) => new ProjectInfo
        {
            Name = j["name"].AsString(),
            Dir = j["dir"].AsString(),
            Temp = j["temp"].AsBool(false),
            Sandbox = Strings(j["sandbox"]),
            Breadcrumbs = Strings(j["breadcrumbs"]),
            Network = NetworkModeText.Parse(j["network"].AsString(WireProtocol.NetworkMode.Private)),
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
