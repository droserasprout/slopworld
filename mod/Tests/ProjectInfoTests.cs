using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class ProjectInfoTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("round trips host networking", RoundTripsHostNetworking);
            yield return ("builds daemon-compatible temporary paths", BuildsTemporaryPaths);
            yield return ("round trips and copies project settings", RoundTripsAndCopiesSettings);
        }

        static void RoundTripsHostNetworking()
        {
            var project = new ProjectInfo
            {
                Name = "repo",
                Dir = "/home/you/repo",
                Network = NetworkMode.Host,
            };

            var wire = JVal.Parse(project.ToJson());
            AssertEx.Equal("host", wire["network"].AsString(),
                           "host network survives project serialization");
            AssertEx.Equal(NetworkMode.Host, ProjectInfo.FromJson(wire).Network,
                           "host network survives project parsing");
        }

        static void BuildsTemporaryPaths()
        {
            AssertEx.Equal("/tmp/slopworld/My-project-foo-bar-baz", ProjectInfo.TempDir(
                "  My project:foo.bar/baz  "), "temporary project slug");
            AssertEx.Equal("/tmp/slopworld/", ProjectInfo.TempDir(null),
                           "empty temporary name keeps the configured root");
        }

        static void RoundTripsAndCopiesSettings()
        {
            var project = new ProjectInfo
            {
                Name = "repo",
                Dir = "/work/repo",
                Temp = true,
                Sandbox = new List<string> { "home", "docs" },
                Breadcrumbs = new List<string> { "read this", "then work" },
                Network = NetworkMode.None,
                Dns = new DnsConfig
                {
                    Mode = DnsMode.Servers,
                    Servers = new List<string> { "8.8.8.8", "1.1.1.1" },
                },
            };

            var wire = JVal.Parse(project.ToJson());
            var parsed = ProjectInfo.FromJson(wire);
            AssertEx.Equal("repo", parsed.Name, "project name");
            AssertEx.Equal("/work/repo", parsed.Dir, "project directory");
            AssertEx.True(parsed.Temp, "temporary flag");
            AssertEx.Sequence(project.Sandbox, parsed.Sandbox, "sandbox presets");
            AssertEx.Sequence(project.Breadcrumbs, parsed.Breadcrumbs, "breadcrumbs");
            AssertEx.Equal(NetworkMode.None, parsed.Network, "none network");
            AssertEx.Sequence(project.Dns.Servers, parsed.Dns.Servers, "custom DNS servers");
            AssertEx.Equal(project.ToJson(), parsed.ToJson(), "project JSON round trip");

            var copy = project.Copy();
            copy.Sandbox[0] = "changed";
            copy.Breadcrumbs.Add("another");
            copy.Dns.Servers[0] = "9.9.9.9";
            AssertEx.Equal("home", project.Sandbox[0], "copy owns sandbox list");
            AssertEx.Equal(2, project.Breadcrumbs.Count, "copy owns breadcrumb list");
            AssertEx.Equal("8.8.8.8", project.Dns.Servers[0], "copy owns DNS list");
        }
    }
}
