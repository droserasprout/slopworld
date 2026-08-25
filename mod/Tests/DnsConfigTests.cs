using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class DnsConfigTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("parses and validates server lists", ParsesAndValidatesServerLists);
            yield return ("round trips DNS modes", RoundTripsDnsModes);
        }

        static void ParsesAndValidatesServerLists()
        {
            bool parsed = DnsConfig.TryParseServers("8.8.8.8, 1.1.1.1", out var servers,
                                                    out var error);
            AssertEx.True(parsed, "valid server list");
            AssertEx.True(error == null, "valid server error");
            AssertEx.Sequence(new[] { "8.8.8.8", "1.1.1.1" }, servers,
                              "valid server list values");

            parsed = DnsConfig.TryParseServers("8.8.8.8;\n1.1.1.1", out servers, out error);
            AssertEx.True(parsed, "mixed separators");
            AssertEx.True(error == null, "mixed separator error");

            parsed = DnsConfig.TryParseServers("", out servers, out error);
            AssertEx.False(parsed, "empty server list");
            AssertEx.Equal("enter at least one IPv4 DNS server", error, "empty list error");
            AssertEx.Sequence(Array.Empty<string>(), servers, "empty list output");

            parsed = DnsConfig.TryParseServers("8.8.8.8 1.1.1.1 9.9.9.9", out _, out error);
            AssertEx.False(parsed, "too many servers");
            AssertEx.Equal("enter at most two IPv4 DNS servers", error, "too many error");

            parsed = DnsConfig.TryParseServers("8.8.8.8,8.8.8.8", out _, out error);
            AssertEx.False(parsed, "duplicate servers");
            AssertEx.Equal("DNS servers must be unique", error, "duplicate error");

            parsed = DnsConfig.TryParseServers("::1", out _, out error);
            AssertEx.False(parsed, "IPv6 server");
            AssertEx.Equal("::1 is not an IPv4 address", error, "IPv6 error");
        }

        static void RoundTripsDnsModes()
        {
            var resolved = DnsConfig.FromJson(JVal.Parse("{\"mode\":\"resolved\"}"));
            AssertEx.True(resolved.IsResolved, "resolved mode");
            AssertEx.Equal("System resolver", resolved.Label,
                           "resolved label");
            AssertEx.Equal("{\"mode\":\"resolved\"}", resolved.ToJson(),
                           "resolved JSON");

            var custom = DnsConfig.FromJson(JVal.Parse(
                "{\"mode\":\"servers\",\"servers\":[\"8.8.8.8\",\"1.1.1.1\"]}"));
            AssertEx.False(custom.IsResolved, "custom mode");
            AssertEx.Sequence(new[] { "8.8.8.8", "1.1.1.1" }, custom.Servers,
                              "custom servers");
            AssertEx.Equal("Custom DNS: 8.8.8.8, 1.1.1.1", custom.Label, "custom label");
            AssertEx.Equal(custom.ToJson(), DnsConfig.FromJson(JVal.Parse(custom.ToJson())).ToJson(),
                           "custom JSON round trip");

            var copy = custom.Copy();
            copy.Servers[0] = "9.9.9.9";
            AssertEx.Equal("8.8.8.8", custom.Servers[0], "DNS copy owns its list");
            AssertEx.True(DnsConfig.FromJson(JVal.Parse("{\"mode\":\"unknown\"}"))
                              .IsResolved, "unknown mode fallback");
        }
    }
}
