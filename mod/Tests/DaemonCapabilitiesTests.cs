using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class DaemonCapabilitiesTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("uses native defaults before capabilities arrive", DefaultsToNative);
            yield return ("reads slopcar feature differences", ReadsSlopcar);
            yield return ("bounds malformed terminal advertisements", BoundsMalformedTerminal);
            yield return ("clamps valid oversized terminal ranges", BoundsOversizedTerminal);
        }

        static void DefaultsToNative()
        {
            var caps = new DaemonCapabilities();
            AssertEx.False(caps.Known, "known");
            AssertEx.Equal("native", caps.Runtime, "runtime");
            AssertEx.True(caps.AudioPlayback, "audio");
            AssertEx.True(caps.Clipboard, "clipboard");
            AssertEx.True(caps.DesktopOpen, "desktop open");
            AssertEx.True(caps.PerSessionLimits, "limits");
            AssertEx.False(caps.HostNetworkIsContainer, "network");
            AssertEx.False(caps.HostTerminalsAreContainer, "terminals");
            AssertEx.Equal("host terminal", caps.TerminalName, "terminal name");
            AssertEx.Equal(10000, caps.Terminal.ScrollbackLines, "initial history limit");
        }

        static void ReadsSlopcar()
        {
            var previous = DaemonCapabilities.Current;
            try
            {
                var caps = DaemonCapabilities.FromWire(ProtobufFixtures.Read<Wire.Capabilities>(JVal.Parse(
                    "{\"runtime\":\"slopcar\",\"audio_playback\":false," +
                    "\"clipboard\":false,\"desktop_open\":false,\"ncspot\":false," +
                    "\"per_session_limits\":false,\"host_network_is_container\":true," +
                    "\"host_terminals_are_container\":true," +
                    "\"terminal\":{\"scrollback_lines\":123,\"min_cols\":22," +
                    "\"max_cols\":302,\"min_rows\":7,\"max_rows\":102}}")));
                AssertEx.Equal("slopcar", caps.Runtime, "runtime");
                AssertEx.True(caps.Known, "known");
                AssertEx.False(caps.AudioPlayback, "audio");
                AssertEx.False(caps.Clipboard, "clipboard");
                AssertEx.False(caps.DesktopOpen, "desktop open");
                AssertEx.False(caps.Ncspot, "ncspot");
                AssertEx.False(caps.PerSessionLimits, "limits");
                AssertEx.True(caps.HostNetworkIsContainer, "network");
                AssertEx.True(caps.HostTerminalsAreContainer, "terminals");
                AssertEx.Equal("container terminals", caps.TerminalNames, "terminal names");
                AssertEx.Equal(123, caps.Terminal.ScrollbackLines, "history capacity");
                AssertEx.Equal(22, caps.Terminal.MinCols, "minimum columns");
                AssertEx.Equal(302, caps.Terminal.MaxCols, "maximum columns");
                AssertEx.Equal(7, caps.Terminal.MinRows, "minimum rows");
                AssertEx.Equal(102, caps.Terminal.MaxRows, "maximum rows");
            }
            finally { DaemonCapabilities.Current = previous; }
        }

        static void BoundsMalformedTerminal()
        {
            var previous = DaemonCapabilities.Current;
            try
            {
                var caps = DaemonCapabilities.FromWire(ProtobufFixtures.Read<Wire.Capabilities>(JVal.Parse(
                    "{\"terminal\":{\"scrollback_lines\":999999," +
                    "\"min_cols\":600,\"max_cols\":500,\"min_rows\":0," +
                    "\"max_rows\":999999}}")));
                AssertEx.Equal(TerminalLimits.ClientMaxScrollbackLines,
                               caps.Terminal.ScrollbackLines, "history allocation cap");
                AssertEx.Equal(20, caps.Terminal.MinCols, "invalid column range fallback");
                AssertEx.Equal(TerminalLimits.ClientMaxCols, caps.Terminal.MaxCols,
                               "invalid column maximum fallback");
                AssertEx.Equal(5, caps.Terminal.MinRows, "invalid row range fallback");
                AssertEx.Equal(TerminalLimits.ClientMaxRows, caps.Terminal.MaxRows,
                               "invalid row maximum fallback");
            }
            finally { DaemonCapabilities.Current = previous; }
        }
        static void BoundsOversizedTerminal()
        {
            var previous = DaemonCapabilities.Current;
            try
            {
                var caps = DaemonCapabilities.FromWire(new Wire.Capabilities { Terminal = new Wire.TerminalCapabilities {
                    ScrollbackLines = 999999, MinCols = 22, MaxCols = 999999, MinRows = 7, MaxRows = 999999 } });
                AssertEx.Equal(22, caps.Terminal.MinCols, "valid minimum columns retained");
                AssertEx.Equal(7, caps.Terminal.MinRows, "valid minimum rows retained");
                AssertEx.Equal(TerminalLimits.ClientMaxCols, caps.Terminal.MaxCols, "oversized maximum columns clamped");
                AssertEx.Equal(TerminalLimits.ClientMaxRows, caps.Terminal.MaxRows, "oversized maximum rows clamped");
            }
            finally { DaemonCapabilities.Current = previous; }
        }
    }
}
