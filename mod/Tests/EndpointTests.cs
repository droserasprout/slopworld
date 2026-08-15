using System;
using System.IO;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class EndpointTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("resolves and normalizes an IPv6 descriptor",
                          ResolvesAndNormalizesIpv6Descriptor);
            yield return ("falls back for missing or invalid descriptors",
                          FallsBackForMissingOrInvalidDescriptors);
            yield return ("normalizes connection info directly", NormalizesConnectionInfoDirectly);
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
                AssertEx.Equal("http://[::1]:8800", connection.BaseUrl,
                               "bracketed IPv6 base URL");
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
            AssertEx.Equal("http://[2001:db8::1]:7717", ipv6.BaseUrl,
                           "direct IPv6 base URL");
            AssertEx.Equal("host.example", host.Host, "ordinary host is preserved");
            AssertEx.Equal("http://host.example:1234", host.BaseUrl,
                           "ordinary base URL");
        }

        static void AssertFallback(ConnectionInfo connection, string message)
        {
            AssertEx.Equal("127.0.0.1", connection.Host, message + " host");
            AssertEx.Equal(7717, connection.Port, message + " port");
            AssertEx.Equal("", connection.Token, message + " token");
        }

        static string TemporaryPath() => Path.Combine(
            Path.GetTempPath(), "slopworld-endpoint-" + Guid.NewGuid().ToString("N") + ".toml");

        static void RestoreEndpoint(string previous)
        {
            Environment.SetEnvironmentVariable("SLOPD_ENDPOINT", previous);
        }
    }
}
