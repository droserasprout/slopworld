using System;
using System.IO;

namespace SlopWorld
{
    public sealed class ConnectionInfo
    {
        public readonly string Host;
        public readonly int Port;
        public readonly string Token;

        public ConnectionInfo(string host, int port, string token)
        {
            Host = (host ?? "").Trim('[', ']');
            Port = port;
            Token = token ?? "";
        }

        public string BaseUrl => $"http://{(Host.IndexOf(':') >= 0 ? "[" + Host + "]" : Host)}:{Port}";
    }

    // The daemon owns the live endpoint. The mod discovers its address and token from
    // endpoint.toml instead of keeping a second connection configuration.
    public static class Endpoint
    {
        const string DefaultHost = SharedDefaults.EndpointHost;
        const int DefaultPort = SharedDefaults.EndpointPort;

        public static ConnectionInfo Resolve()
        {
            try
            {
                var text = File.ReadAllText(Path());
                var value = Toml.ParseFlat(text);
                var uri = new Uri(value["url"], UriKind.Absolute);
                if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) ||
                    uri.Port <= 0)
                    return Fallback();

                return new ConnectionInfo(uri.Host, uri.Port, value["token"]);
            }
            catch
            {
                return Fallback();
            }
        }

        static ConnectionInfo Fallback() => new ConnectionInfo(DefaultHost, DefaultPort, "");

        static string Path()
        {
            var explicitPath = Environment.GetEnvironmentVariable("SLOPD_ENDPOINT");
            if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;

            var root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(root))
                root = System.Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile) + "/.config";
            return System.IO.Path.Combine(root, "slopworld", "endpoint.toml");
        }
    }
}
