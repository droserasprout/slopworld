using System.Collections.Generic;

namespace SlopWorld
{
    // Per-agent resource caps, mirrored from the daemon's Limits. Every field is optional; a
    // missing one inherits the project, or has no cap on the project itself. Copies are by value.
    public struct SessionLimits
    {
        public int? MemoryMb;
        public int? Pids;
        public int? Nofile;
        public int? CpuPct;

        public bool IsEmpty =>
            !MemoryMb.HasValue && !Pids.HasValue && !Nofile.HasValue && !CpuPct.HasValue;

        static int? Num(JVal v) => v.IsNull ? (int?)null : v.AsInt(0);

        public static SessionLimits FromJson(JVal j) => new SessionLimits
        {
            MemoryMb = Num(j["memory_mb"]),
            Pids = Num(j["pids"]),
            Nofile = Num(j["nofile"]),
            CpuPct = Num(j["cpu_pct"]),
        };

        // Only the set fields ride along, so an unset cap is absent rather than zero - the
        // daemon reads a missing field as "no cap", a zero as a session that cannot start.
        public string ToJson()
        {
            var parts = new List<string>();
            if (MemoryMb.HasValue) parts.Add($"\"memory_mb\":{MemoryMb.Value}");
            if (Pids.HasValue) parts.Add($"\"pids\":{Pids.Value}");
            if (Nofile.HasValue) parts.Add($"\"nofile\":{Nofile.Value}");
            if (CpuPct.HasValue) parts.Add($"\"cpu_pct\":{CpuPct.Value}");
            return "{" + string.Join(",", parts.ToArray()) + "}";
        }
    }
}
