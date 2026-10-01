using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class TraceRecordTests
    {
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
                AssertEx.Equal("performance,latency", string.Join(",", output), "drain retains order");
                output.Clear();
                for (int i = 0; i < 1025; i++) TerminalLatency.Record("record");
                AssertEx.Equal(0, output.Count, "overflow cannot write early");
                TerminalLatency.Flush();
                AssertEx.Equal(1025, output.Count, "bounded records plus overflow diagnostic");
                AssertEx.True(output[1024].Contains("dropped_records=1"), "overflow is reported");
                output.Clear();
                TerminalLatency.Flush();
                AssertEx.Equal(0, output.Count, "drain clears records and overflow count");
            }
            finally { TerminalLatency.Write = previous; }
        }
    }
}
