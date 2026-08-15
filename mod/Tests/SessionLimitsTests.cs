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
            var limits = SessionLimits.FromJson(JVal.Parse("{}"));

            AssertEx.True(limits.IsEmpty, "missing limits are empty");
            AssertEx.True(!limits.MemoryMb.HasValue && !limits.Pids.HasValue &&
                          !limits.Nofile.HasValue && !limits.CpuPct.HasValue,
                          "missing limits remain unset");
            AssertEx.Equal("{}", limits.ToJson(), "empty limits JSON");
        }

        static void RoundTripsOptionalLimits()
        {
            var limits = SessionLimits.FromJson(JVal.Parse(
                "{\"memory_mb\":512,\"pids\":64,\"nofile\":null,\"cpu_pct\":75}"));

            AssertEx.False(limits.IsEmpty, "set limits are not empty");
            AssertEx.Equal(512, limits.MemoryMb.Value, "memory limit");
            AssertEx.Equal(64, limits.Pids.Value, "pid limit");
            AssertEx.False(limits.Nofile.HasValue, "null nofile limit");
            AssertEx.Equal(75, limits.CpuPct.Value, "CPU limit");
            AssertEx.Equal("{\"memory_mb\":512,\"pids\":64,\"cpu_pct\":75}",
                           limits.ToJson(), "only set limits serialize");

            var roundTrip = SessionLimits.FromJson(JVal.Parse(limits.ToJson()));
            AssertEx.Equal(limits.MemoryMb, roundTrip.MemoryMb, "memory round trip");
            AssertEx.Equal(limits.Pids, roundTrip.Pids, "pids round trip");
            AssertEx.Equal(limits.Nofile, roundTrip.Nofile, "nofile round trip");
            AssertEx.Equal(limits.CpuPct, roundTrip.CpuPct, "CPU round trip");
        }
    }
}
