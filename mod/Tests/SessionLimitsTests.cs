using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class SessionLimitsTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("recognizes empty limits", RecognizesEmptyLimits);
            yield return ("round trips optional limits", RoundTripsOptionalLimits);
        }

        static void RecognizesEmptyLimits()
        {
            var limits = SessionLimits.FromWire(ProtobufFixtures.Read<Wire.Limits>(JVal.Parse("{}")));

            AssertEx.True(limits.IsEmpty, "missing limits are empty");
            AssertEx.False(limits.MemoryMb.HasValue, "missing memory cap stays unset");
            AssertEx.False(limits.Pids.HasValue, "missing process cap stays unset");
            AssertEx.False(limits.Nofile.HasValue, "missing file cap stays unset");
            AssertEx.False(limits.CpuPct.HasValue, "missing CPU cap stays unset");
            var absent = SessionLimits.FromWire(null);
            AssertEx.True(absent.IsEmpty, "null limits are empty");
            AssertEx.Equal("{}", absent.ToJson(), "null limits serialize empty");
            AssertEx.Equal("{}", limits.ToJson(), "empty limits JSON");
        }

        static void RoundTripsOptionalLimits()
        {
            var limits = SessionLimits.FromWire(ProtobufFixtures.Read<Wire.Limits>(JVal.Parse(
                "{\"memory_mb\":512,\"pids\":64,\"nofile\":null,\"cpu_pct\":75}")));

            AssertEx.False(limits.IsEmpty, "set limits are not empty");
            AssertEx.Equal(512U, limits.MemoryMb.Value, "memory limit");
            AssertEx.Equal(64U, limits.Pids.Value, "pid limit");
            AssertEx.False(limits.Nofile.HasValue, "null nofile limit");
            AssertEx.Equal(75U, limits.CpuPct.Value, "CPU limit");
            AssertEx.Equal("{\"memory_mb\":512,\"pids\":64,\"cpu_pct\":75}",
                           limits.ToJson(), "only set limits serialize");

            var roundTrip = SessionLimits.FromWire(ProtobufFixtures.Read<Wire.Limits>(JVal.Parse(limits.ToJson())));
            AssertEx.Equal(limits.MemoryMb, roundTrip.MemoryMb, "memory round trip");
            AssertEx.Equal(limits.Pids, roundTrip.Pids, "pids round trip");
            AssertEx.Equal(limits.Nofile, roundTrip.Nofile, "nofile round trip");
            AssertEx.Equal(limits.CpuPct, roundTrip.CpuPct, "CPU round trip");
        }
    }
}
