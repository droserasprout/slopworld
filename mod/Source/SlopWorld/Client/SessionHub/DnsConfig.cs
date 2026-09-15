using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace SlopWorld
{
    public enum DnsMode { Resolved, Servers }

    // DNS is separate from network reach. Resolved follows the daemon's current resolv.conf;
    // Servers is an explicit list passed to pasta.
    public class DnsConfig
    {
        public DnsMode Mode = DnsMode.Resolved;
        public List<string> Servers = new List<string>();

        public bool IsResolved => Mode == DnsMode.Resolved;

        public string Label => IsResolved
            ? "System resolver"
            : Servers.Count == 0
                ? "Custom DNS (empty)"
                : "Custom DNS: " + string.Join(", ", Servers.ToArray());

        public static DnsConfig FromJson(JVal j)
        {
            var dns = new DnsConfig();
            if (j == null || j.IsNull || j["mode"].AsString() != WireProtocol.DnsMode.Servers) return dns;
            dns.Mode = DnsMode.Servers;
            dns.Servers = j["servers"].Items.Select(i => i.AsString()).ToList();
            return dns;
        }

        public DnsConfig Copy() => new DnsConfig
        {
            Mode = Mode,
            Servers = new List<string>(Servers),
        };

        public string ToJson() => IsResolved
            ? "{\"mode\":\"" + WireProtocol.DnsMode.Resolved + "\"}"
            : "{\"mode\":\"" + WireProtocol.DnsMode.Servers + "\",\"servers\":[" +
              string.Join(",", Servers.Select(JVal.Q).ToArray()) + "]}";

        public static DnsConfig Resolved() => new DnsConfig();
        public static DnsConfig Custom() => new DnsConfig { Mode = DnsMode.Servers };

        public static bool TryParseServers(string text, out List<string> servers, out string error)
        {
            servers = new List<string>();
            error = null;
            var parts = (text ?? "").Split(new[] { ',', ';', ' ', '\t', '\r', '\n' },
                System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                error = "enter at least one IPv4 DNS server";
                return false;
            }
            if (parts.Length > 2)
            {
                error = "enter at most two IPv4 DNS servers";
                return false;
            }
            foreach (string part in parts)
            {
                if (!IPAddress.TryParse(part, out var address) ||
                    address.AddressFamily != AddressFamily.InterNetwork)
                {
                    error = $"{part} is not an IPv4 address";
                    return false;
                }
                string normalized = address.ToString();
                if (servers.Contains(normalized))
                {
                    error = "DNS servers must be unique";
                    return false;
                }
                servers.Add(normalized);
            }
            return true;
        }
    }
}
