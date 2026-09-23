using System.Collections.Generic;

namespace SlopWorld
{
    // Per-agent resource caps, mirrored from the daemon's Limits. Every field is optional. A
    // missing one means that no cap is configured. Copies are by value.
    public struct SessionLimits
    {
        public int? MemoryMb;
        public int? Pids;
        public int? Nofile;
        public int? CpuPct;

        public bool IsEmpty =>
            !MemoryMb.HasValue && !Pids.HasValue && !Nofile.HasValue && !CpuPct.HasValue;

        public static SessionLimits FromWire(Wire.Limits j) => j == null ? default : new SessionLimits
        {
            MemoryMb = j.HasMemoryMb ? (int?)j.MemoryMb : null,
            Pids = j.HasPids ? (int?)j.Pids : null,
            Nofile = j.HasNofile ? (int?)j.Nofile : null,
            CpuPct = j.HasCpuPct ? (int?)j.CpuPct : null,
        };
        public Wire.Limits ToWire()
        {
            var value = new Wire.Limits();
            if (MemoryMb.HasValue) value.MemoryMb = checked((uint)MemoryMb.Value);
            if (Pids.HasValue) value.Pids = checked((uint)Pids.Value);
            if (Nofile.HasValue) value.Nofile = checked((uint)Nofile.Value);
            if (CpuPct.HasValue) value.CpuPct = checked((uint)CpuPct.Value);
            return value;
        }
    }
}
