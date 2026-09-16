using Verse;

namespace SlopWorld
{
    // Project defaults, agent overrides and recipe choices use the same buffers and validation.
    public sealed class ResourceLimitsForm
    {
        string _memory, _pids, _nofile, _cpu;

        public ResourceLimitsForm(SessionLimits value)
        {
            _memory = Text(value.MemoryMb);
            _pids = Text(value.Pids);
            _nofile = Text(value.Nofile);
            _cpu = Text(value.CpuPct);
        }

        public SessionLimits Draw(Listing_Standard l)
        {
            l.Label("Memory (MiB)");
            _memory = UiControls.Field(l, "limits.memory", _memory);
            l.Label("Max processes and threads");
            _pids = UiControls.Field(l, "limits.pids", _pids);
            l.Label("Open files per process");
            _nofile = UiControls.Field(l, "limits.nofile", _nofile);
            l.Label("CPU (% of one core)");
            _cpu = UiControls.Field(l, "limits.cpu", _cpu);
            return new SessionLimits
            {
                MemoryMb = Value(_memory),
                Pids = Value(_pids),
                Nofile = Value(_nofile),
                CpuPct = Value(_cpu)
            };
        }

        public bool TrySave(out SessionLimits value, out string error)
        {
            value = new SessionLimits();
            error = null;
            if (!Valid(_memory) || !Valid(_pids) || !Valid(_nofile) || !Valid(_cpu))
            {
                error = "Resource limits must be positive whole numbers, or blank to inherit.";
                return false;
            }
            value = new SessionLimits
            {
                MemoryMb = Value(_memory),
                Pids = Value(_pids),
                Nofile = Value(_nofile),
                CpuPct = Value(_cpu)
            };
            return true;
        }

        static bool Valid(string text) => string.IsNullOrWhiteSpace(text) || Value(text).HasValue;
        static int? Value(string text) => int.TryParse((text ?? "").Trim(), out var n) && n > 0 ? n : (int?)null;
        static string Text(int? value) => value?.ToString() ?? "";
    }
}
