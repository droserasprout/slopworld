using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class TraceRecordTests
    {
        public static void InputTracingMatchesDebugModeAndResetSettlesSamples()
        {
            var previous = TerminalLatency.Write;
            var output = new List<string>();
            TerminalLatency.Write = output.Add;
            try
            {
                TerminalLatency.Timeline.Reset();
                TerminalLatency.Flush();
                output.Clear();
                var keys = new Wire.ClientMessage { Keys = new Wire.KeysReq { Name = "pane" } };
                var paste = new Wire.ClientMessage { Paste = new Wire.PasteReq { Name = "pane" } };
                var mouse = new Wire.ClientMessage { Mouse = new Wire.MouseReq { Name = "pane" } };
                TerminalLatency.Begin(keys);
                TerminalLatency.Begin(paste);
                TerminalLatency.Begin(mouse);
                TerminalLatency.Begin(
                    new Wire.ClientMessage { Sub = new Wire.NameReq { Name = "pane" } }
                );
                AssertEx.Equal(
                    TerminalLatency.Enabled,
                    keys.Keys.TraceId.Length != 0,
                    "keys trace switch"
                );
                AssertEx.Equal(
                    TerminalLatency.Enabled,
                    paste.Paste.TraceId.Length != 0,
                    "paste trace switch"
                );
                AssertEx.Equal(
                    TerminalLatency.Enabled,
                    mouse.Mouse.TraceId.Length != 0,
                    "mouse trace switch"
                );
                AssertEx.Equal(
                    TerminalLatency.Enabled ? 3 : 0,
                    TerminalLatency.Timeline.Count,
                    "only input commands create samples"
                );
                AssertEx.Equal(0, output.Count, "trace producers do not write synchronously");
                TerminalLatency.Timeline.Reset();
                TerminalLatency.Flush();
                AssertEx.Equal(
                    TerminalLatency.Enabled ? 6 : 0,
                    output.Count,
                    "reset publishes one outcome per input"
                );
                AssertEx.Equal(0, TerminalLatency.Timeline.Count, "reset releases samples");
                output.Clear();
                TerminalLatency.Flush();
                AssertEx.Equal(0, output.Count, "second flush does not replay records");
            }
            finally
            {
                TerminalLatency.Write = previous;
            }
        }

        public static void RecordsWaitForDrainAndOverflowIsBounded()
        {
            var previous = TerminalLatency.Write;
            var output = new List<string>();
            TerminalLatency.Write = output.Add;
            try
            {
                TerminalLatency.Flush();
                output.Clear();
                TerminalLatency.Record("performance");
                TerminalLatency.Record("latency");
                AssertEx.Equal(0, output.Count, "producers cannot perform sink I/O");
                TerminalLatency.Flush();
                AssertEx.Equal(
                    "performance,latency",
                    string.Join(",", output),
                    "drain retains order"
                );
                output.Clear();
                for (int i = 0; i < 1025; i++)
                    TerminalLatency.Record("record");
                AssertEx.Equal(0, output.Count, "overflow cannot write early");
                TerminalLatency.Flush();
                AssertEx.Equal(1025, output.Count, "bounded records plus overflow diagnostic");
                AssertEx.True(output[1024].Contains("dropped_records=1"), "overflow is reported");
                output.Clear();
                TerminalLatency.Flush();
                AssertEx.Equal(0, output.Count, "drain clears records and overflow count");
            }
            finally
            {
                TerminalLatency.Write = previous;
            }
        }
    }
}
