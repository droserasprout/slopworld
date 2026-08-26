using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class ProjectInfoTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("round trips host networking", RoundTripsHostNetworking);
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
    }
}
