using System;
using System.Collections.Generic;
using System.IO;

namespace SlopWorld.Tests
{
    static class EndpointTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return (
                "resolves and normalizes an IPv6 descriptor",
                ResolvesAndNormalizesIpv6Descriptor
            );
            yield return (
                "falls back for missing or invalid descriptors",
                FallsBackForMissingOrInvalidDescriptors
            );
            yield return ("normalizes connection info directly", NormalizesConnectionInfoDirectly);
            yield return ("finds the descriptor below XDG config", FindsXdgDescriptor);
            yield return ("application roots override XDG paths", ApplicationRootsOverrideXdg);
        }

        static void ResolvesAndNormalizesIpv6Descriptor()
        {
            string path = TemporaryPath();
            string previous = Environment.GetEnvironmentVariable("SLOPD_ENDPOINT");
            try
            {
                File.WriteAllText(path, "url = \"http://[::1]:8800\"\ntoken = \"secret\"\n");
                Environment.SetEnvironmentVariable("SLOPD_ENDPOINT", path);

                var connection = Endpoint.Resolve();

                AssertEx.Equal("::1", connection.Host, "IPv6 host normalization");
                AssertEx.Equal(8800, connection.Port, "descriptor port");
                AssertEx.Equal("secret", connection.Token, "descriptor token");
                AssertEx.Equal("http://[::1]:8800", connection.BaseUrl, "bracketed IPv6 base URL");
            }
            finally
            {
                RestoreEndpoint(previous);
                File.Delete(path);
            }
        }

        static void FallsBackForMissingOrInvalidDescriptors()
        {
            string path = TemporaryPath();
            string previous = Environment.GetEnvironmentVariable("SLOPD_ENDPOINT");
            try
            {
                Environment.SetEnvironmentVariable("SLOPD_ENDPOINT", path);
                AssertFallback(Endpoint.Resolve(), "missing descriptor");

                File.WriteAllText(path, "url = \"https://example.test:9443\"\ntoken = \"bad\"\n");
                AssertFallback(Endpoint.Resolve(), "non-http descriptor");
            }
            finally
            {
                RestoreEndpoint(previous);
                File.Delete(path);
            }
        }

        static void NormalizesConnectionInfoDirectly()
        {
            var ipv6 = new ConnectionInfo("[2001:db8::1]", 7717, null);
            var host = new ConnectionInfo("host.example", 1234, "token");

            AssertEx.Equal("2001:db8::1", ipv6.Host, "brackets are removed");
            AssertEx.Equal("", ipv6.Token, "null token becomes empty");
            AssertEx.Equal("http://[2001:db8::1]:7717", ipv6.BaseUrl, "direct IPv6 base URL");
            AssertEx.Equal("host.example", host.Host, "ordinary host is preserved");
            AssertEx.Equal("http://host.example:1234", host.BaseUrl, "ordinary base URL");
        }

        static void FindsXdgDescriptor()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "slopworld-config-" + Guid.NewGuid().ToString("N")
            );
            string previousEndpoint = Environment.GetEnvironmentVariable("SLOPD_ENDPOINT");
            string previousXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            string previousConfig = Environment.GetEnvironmentVariable("SLOPD_CONFIG_ROOT");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "slopworld"));
                File.WriteAllText(
                    Path.Combine(root, "slopworld", "endpoint.toml"),
                    "url = \"http://example.test:8822\"\ntoken = \"xdg\"\n"
                );
                Environment.SetEnvironmentVariable("SLOPD_ENDPOINT", null);
                Environment.SetEnvironmentVariable("SLOPD_CONFIG_ROOT", null);
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", root);

                var connection = Endpoint.Resolve();
                AssertEx.Equal("example.test", connection.Host, "XDG descriptor host");
                AssertEx.Equal(8822, connection.Port, "XDG descriptor port");
                AssertEx.Equal("xdg", connection.Token, "XDG descriptor token");
            }
            finally
            {
                Environment.SetEnvironmentVariable("SLOPD_ENDPOINT", previousEndpoint);
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousXdg);
                Environment.SetEnvironmentVariable("SLOPD_CONFIG_ROOT", previousConfig);
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        static void ApplicationRootsOverrideXdg()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "slopworld-roots-" + Guid.NewGuid().ToString("N")
            );
            string[] variables =
            {
                "SLOPD_ENDPOINT",
                "SLOPD_CONFIG_ROOT",
                "SLOPD_DATA",
                "XDG_CONFIG_HOME",
                "XDG_DATA_HOME",
            };
            var previous = new Dictionary<string, string>();
            foreach (string variable in variables)
                previous[variable] = Environment.GetEnvironmentVariable(variable);
            try
            {
                string config = Path.Combine(root, "config");
                string data = Path.Combine(root, "data");
                Directory.CreateDirectory(config);
                File.WriteAllText(
                    Path.Combine(config, "endpoint.toml"),
                    "url = \"http://root.test:8823\"\ntoken = \"root\"\n"
                );
                Environment.SetEnvironmentVariable("SLOPD_ENDPOINT", null);
                Environment.SetEnvironmentVariable("SLOPD_CONFIG_ROOT", config);
                Environment.SetEnvironmentVariable("SLOPD_DATA", data);
                Environment.SetEnvironmentVariable(
                    "XDG_CONFIG_HOME",
                    Path.Combine(root, "unused-config")
                );
                Environment.SetEnvironmentVariable(
                    "XDG_DATA_HOME",
                    Path.Combine(root, "unused-data")
                );
                AssertEx.Equal(
                    "root.test",
                    Endpoint.Resolve().Host,
                    "descriptor follows config root"
                );
                AssertEx.Equal(
                    Path.Combine(data, "jukebox.toml"),
                    ApplicationPaths.LikesPath,
                    "likes follow data root"
                );
                string explicitEndpoint = Path.Combine(root, "explicit.toml");
                File.WriteAllText(
                    explicitEndpoint,
                    "url = \"http://explicit.test:8824\"\ntoken = \"explicit\"\n"
                );
                Environment.SetEnvironmentVariable("SLOPD_ENDPOINT", explicitEndpoint);
                AssertEx.Equal(
                    "explicit.test",
                    Endpoint.Resolve().Host,
                    "explicit descriptor wins over config root"
                );
            }
            finally
            {
                foreach (string variable in variables)
                    Environment.SetEnvironmentVariable(variable, previous[variable]);
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        static void AssertFallback(ConnectionInfo connection, string message)
        {
            AssertEx.Equal("127.0.0.1", connection.Host, message + " host");
            AssertEx.Equal(7717, connection.Port, message + " port");
            AssertEx.Equal("", connection.Token, message + " token");
        }

        static string TemporaryPath() =>
            Path.Combine(
                Path.GetTempPath(),
                "slopworld-endpoint-" + Guid.NewGuid().ToString("N") + ".toml"
            );

        static void RestoreEndpoint(string previous)
        {
            Environment.SetEnvironmentVariable("SLOPD_ENDPOINT", previous);
        }
    }
}
