using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class NetworkModeTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("parses and names modes", ParsesAndNamesModes);
            yield return ("labels modes", LabelsModes);
        }

        static void ParsesAndNamesModes()
        {
            AssertEx.Equal(NetworkMode.None, NetworkModeText.Parse(" NONE "),
                           "none parsing");
            AssertEx.Equal(NetworkMode.Host, NetworkModeText.Parse("Host"),
                           "host parsing");
            AssertEx.Equal(NetworkMode.Private, NetworkModeText.Parse("private"),
                           "private parsing");
            AssertEx.Equal(NetworkMode.Private, NetworkModeText.Parse("future-mode"),
                           "unknown mode fallback");
            AssertEx.Equal(NetworkMode.Private, NetworkModeText.Parse(null),
                           "null mode fallback");

            AssertEx.Equal("none", NetworkModeText.Name(NetworkMode.None), "none name");
            AssertEx.Equal("private", NetworkModeText.Name(NetworkMode.Private),
                           "private name");
            AssertEx.Equal("host", NetworkModeText.Name(NetworkMode.Host), "host name");
        }

        static void LabelsModes()
        {
            AssertEx.Equal("No network", NetworkModeText.Label(NetworkMode.None),
                           "none label");
            AssertEx.Equal("Private network (Internet, no host loopback)",
                           NetworkModeText.Label(NetworkMode.Private), "private label");
            AssertEx.Equal("Host network (daemon’s local network)",
                           NetworkModeText.Label(NetworkMode.Host), "host label");
            AssertEx.Equal("no network", NetworkModeText.ShortLabel(NetworkMode.None),
                           "none short label");
            AssertEx.Equal("private network", NetworkModeText.ShortLabel(NetworkMode.Private),
                           "private short label");
            AssertEx.Equal("host network", NetworkModeText.ShortLabel(NetworkMode.Host),
                           "host short label");
        }

    }
}
