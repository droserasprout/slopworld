using Verse;

namespace SlopWorld
{
    // Keep the editable text separate from the parsed value: an empty field means no cap.
    public sealed class ResourceLimitsForm
    {
        sealed class LimitField
        {
            public string Text;

            public LimitField(uint? value) => Text = value?.ToString() ?? "";

            public uint? Value => uint.TryParse((Text ?? "").Trim(), out var n) && n > 0 ? n : (uint?)null;
        }

        readonly LimitField _memory, _pids, _nofile, _cpu;

        public ResourceLimitsForm(SessionLimits value)
        {
            _memory = new LimitField(value.MemoryMb);
            _pids = new LimitField(value.Pids);
            _nofile = new LimitField(value.Nofile);
            _cpu = new LimitField(value.CpuPct);
        }

        public SessionLimits Draw(Listing_Standard l)
        {
            DrawField(l, "Memory (MiB)", "limits.memory", _memory);
            DrawField(l, "Max processes and threads", "limits.pids", _pids);
            DrawField(l, "Open files per process", "limits.nofile", _nofile);
            DrawField(l, "CPU (% of one core)", "limits.cpu", _cpu);
            return Values();
        }

        void DrawField(Listing_Standard l, string label, string id, LimitField field)
        {
            l.Label(label);
            field.Text = UiControls.Field(l, id, field.Text);
        }

        SessionLimits Values() => new SessionLimits
        {
            MemoryMb = _memory.Value,
            Pids = _pids.Value,
            Nofile = _nofile.Value,
            CpuPct = _cpu.Value
        };

        public bool TrySave(out SessionLimits value, out string error)
        {
            value = Values();
            error = null;
            foreach (var field in new[] { _memory, _pids, _nofile, _cpu })
            {
                if (string.IsNullOrWhiteSpace(field.Text) || field.Value.HasValue) continue;
                error = "Resource limits must be positive whole numbers, or blank for no configured cap.";
                return false;
            }
            return true;
        }
    }
}
