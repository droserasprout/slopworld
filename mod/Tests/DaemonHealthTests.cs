using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class DaemonHealthTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("reads health metadata", ReadsHealthMetadata);
            yield return ("defaults missing health metadata", DefaultsMissingMetadata);
        }

        static void ReadsHealthMetadata()
        {
            var health = DaemonHealth.FromJson(JVal.Parse(
                "{\"ok\":true,\"version\":\"0.0.1\",\"hostname\":\"slopbox\"}"));
            AssertEx.True(health.Known, "known");
            AssertEx.Equal("0.0.1", health.Version, "version");
            AssertEx.Equal("slopbox", health.Hostname, "hostname");
        }

        static void DefaultsMissingMetadata()
        {
            var health = DaemonHealth.FromJson(null);
            AssertEx.False(health.Known, "known");
            AssertEx.Equal("?", health.Version, "version");
            AssertEx.Equal("?", health.Hostname, "hostname");
        }
    }
}
