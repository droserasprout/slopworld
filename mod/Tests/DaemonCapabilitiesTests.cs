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
            AssertEx.Equal("host terminal", caps.TerminalName, "terminal name");
            AssertEx.Equal(10000, caps.Terminal.ScrollbackLines, "legacy history limit");
        }

        static void ReadsSlopcar()
        {
            var caps = DaemonCapabilities.FromJson(JVal.Parse(
                "{\"runtime\":\"slopcar\",\"audio_playback\":false," +
                "\"clipboard\":false,\"desktop_open\":false," +
                "\"per_session_limits\":false,\"host_network_is_container\":true," +
                "\"host_terminals_are_container\":true," +
                "\"terminal\":{\"scrollback_lines\":123,\"min_cols\":22," +
                "\"max_cols\":302,\"min_rows\":7,\"max_rows\":102}}"));
            AssertEx.Equal("slopcar", caps.Runtime, "runtime");
            AssertEx.True(caps.Known, "known");
            AssertEx.False(caps.AudioPlayback, "audio");
            AssertEx.False(caps.PerSessionLimits, "limits");
            AssertEx.True(caps.HostNetworkIsContainer, "network");
            AssertEx.True(caps.HostTerminalsAreContainer, "terminals");
            AssertEx.Equal("container terminals", caps.TerminalNames, "terminal names");
            AssertEx.Equal(123, caps.Terminal.ScrollbackLines, "history capacity");
            AssertEx.Equal(22, caps.Terminal.MinCols, "minimum columns");
            AssertEx.Equal(302, caps.Terminal.MaxCols, "maximum columns");
            AssertEx.Equal(7, caps.Terminal.MinRows, "minimum rows");
            AssertEx.Equal(102, caps.Terminal.MaxRows, "maximum rows");
            // The production parser updates the process-wide capability read model. Restore the
            // legacy native view so game-free tests that exercise history/layout stay isolated.
            DaemonCapabilities.FromJson(null);
        }
    }
}
