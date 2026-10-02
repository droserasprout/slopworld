using System.Collections.Generic;

namespace SlopWorld
{
    // Per-agent resource caps, mirrored from the daemon's Limits. Every field is optional. A
    // missing one means that no cap is configured. Copies are by value.
    public struct SessionLimits
    {
        public uint? MemoryMb;
        public uint? Pids;
        public uint? Nofile;
        public uint? CpuPct;

        public bool IsEmpty =>
            !MemoryMb.HasValue && !Pids.HasValue && !Nofile.HasValue && !CpuPct.HasValue;

        public static SessionLimits FromWire(Wire.Limits j) => j == null ? default : new SessionLimits
        {
            MemoryMb = j.HasMemoryMb ? (uint?)j.MemoryMb : null,
            Pids = j.HasPids ? (uint?)j.Pids : null,
            Nofile = j.HasNofile ? (uint?)j.Nofile : null,
            CpuPct = j.HasCpuPct ? (uint?)j.CpuPct : null,
        };
        public Wire.Limits ToWire()
        {
            var value = new Wire.Limits();
            if (MemoryMb.HasValue) value.MemoryMb = MemoryMb.Value;
            if (Pids.HasValue) value.Pids = Pids.Value;
            if (Nofile.HasValue) value.Nofile = Nofile.Value;
            if (CpuPct.HasValue) value.CpuPct = CpuPct.Value;
            return value;
        }
    }
}
