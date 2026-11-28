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
        public readonly string PathPrefix = "";

        public ConnectionInfo(string host, int port, string token, bool useTls = false)
        {
            Host = (host ?? "").Trim('[', ']');
            Port = port;
            Token = token ?? "";
            UseTls = useTls;
        }

        internal ConnectionInfo(Uri uri, string token)
            : this(uri.Host, uri.Port, token, uri.Scheme == "https")
        {
            PathPrefix = uri.AbsolutePath.TrimEnd('/');
        }

        internal static string FormatAuthority(string host, int port) =>
            FormattableString.Invariant($"{(host.Contains(':') ? "[" + host + "]" : host)}:{port}");

        public string BaseUrl =>
            $"{(UseTls ? "https" : "http")}://{FormatAuthority(Host, Port)}{PathPrefix}";
    }

    // The selected descriptor supplies the address and token for both transports.
    public static class Endpoint
    {
        // Bootstrap-only values. Once connected, daemon metadata is authoritative for policy.
        const string DefaultHost = "127.0.0.1";
        const int DefaultPort = 7717;

        internal static Func<ConnectionInfo> Provider { get; set; } = ReadDescriptor;

        public static ConnectionInfo Resolve() => Provider();

        static ConnectionInfo ReadDescriptor()
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
                    || uri.UserInfo.Length != 0
                    || uri.Query.Length != 0
                    || uri.Fragment.Length != 0
                )
                    return Fallback();

                return new ConnectionInfo(uri, value["token"]);
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
