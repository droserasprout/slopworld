using System;
using System.IO;

namespace SlopWorld
{
    public sealed class ConnectionInfo
    {
        public readonly string Host;
        public readonly int Port;
        public readonly string Token;
        public readonly bool UseTls;

        public ConnectionInfo(string host, int port, string token, bool useTls = false)
        {
            Host = (host ?? "").Trim('[', ']');
            Port = port;
            Token = token ?? "";
            UseTls = useTls;
        }

        public string BaseUrl =>
            $"{(UseTls ? "https" : "http")}://{(Host.IndexOf(':') >= 0 ? "[" + Host + "]" : Host)}:{Port}";
    }

    // The daemon owns the live endpoint. The mod discovers its address and token from
    // endpoints/local.toml instead of keeping a second connection configuration.
    public static class Endpoint
    {
        // Bootstrap-only values. Once connected, daemon metadata is authoritative for policy.
        const string DefaultHost = "127.0.0.1";
        const int DefaultPort = 7717;

        public static ConnectionInfo Resolve()
        {
            try
            {
                var text = File.ReadAllText(DescriptorPath());
                var value = Toml.ParseFlat(text);
                var uri = new Uri(value["url"], UriKind.Absolute);
                if (
                    (
                        !string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                    )
                    || uri.Port <= 0
                )
                    return Fallback();

                return new ConnectionInfo(
                    uri.Host,
                    uri.Port,
                    value["token"],
                    uri.Scheme == "https"
                );
            }
            catch
            {
                return Fallback();
            }
        }

        static ConnectionInfo Fallback() => new ConnectionInfo(DefaultHost, DefaultPort, "");

        static string DescriptorPath()
        {
            var explicitPath = Environment.GetEnvironmentVariable("SLOPD_ENDPOINT");
            if (!string.IsNullOrEmpty(explicitPath))
                return explicitPath;

            return Path.Combine(ApplicationPaths.ConfigRoot, "endpoints", "local.toml");
        }
    }
}
