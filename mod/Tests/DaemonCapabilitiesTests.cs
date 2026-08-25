using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class DaemonCapabilitiesTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("defaults old daemons to native features", DefaultsToNative);
            yield return ("reads slopcar feature differences", ReadsSlopcar);
        }

        static void DefaultsToNative()
        {
            var caps = DaemonCapabilities.FromJson(null);
            AssertEx.False(caps.Known, "known");
            AssertEx.Equal("native", caps.Runtime, "runtime");
            AssertEx.True(caps.AudioPlayback, "audio");
            AssertEx.True(caps.Clipboard, "clipboard");
            AssertEx.True(caps.DesktopOpen, "desktop open");
            AssertEx.True(caps.PerSessionLimits, "limits");
            AssertEx.False(caps.HostNetworkIsContainer, "network");
            AssertEx.False(caps.HostTerminalsAreContainer, "terminals");
        }

        static void ReadsSlopcar()
        {
            var caps = DaemonCapabilities.FromJson(JVal.Parse(
                "{\"runtime\":\"slopcar\",\"audio_playback\":false," +
                "\"clipboard\":false,\"desktop_open\":false," +
                "\"per_session_limits\":false,\"host_network_is_container\":true," +
                "\"host_terminals_are_container\":true}"));
            AssertEx.Equal("slopcar", caps.Runtime, "runtime");
            AssertEx.True(caps.Known, "known");
            AssertEx.False(caps.AudioPlayback, "audio");
            AssertEx.False(caps.PerSessionLimits, "limits");
            AssertEx.True(caps.HostNetworkIsContainer, "network");
            AssertEx.True(caps.HostTerminalsAreContainer, "terminals");
        }
    }
}
