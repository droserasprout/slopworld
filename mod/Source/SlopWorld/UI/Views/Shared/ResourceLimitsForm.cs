using Verse;

namespace SlopWorld
{
    // Keep mode separate from raw text: an empty custom field is invalid, not inheritance.
    public sealed class ResourceLimitsForm
    {
        sealed class LimitField
        {
            public string Text;
            public bool Custom;

            public LimitField(int? value)
            {
                Text = value?.ToString() ?? "";
                Custom = value.HasValue;
            }

            public int? Value => int.TryParse((Text ?? "").Trim(), out var n) && n > 0 ? n : (int?)null;
        }

        readonly LimitField _memory, _pids, _nofile, _cpu;
        bool _overrides;

        public ResourceLimitsForm(SessionLimits value)
        {
            _memory = new LimitField(value.MemoryMb);
            _pids = new LimitField(value.Pids);
            _nofile = new LimitField(value.Nofile);
            _cpu = new LimitField(value.CpuPct);
        }

        public SessionLimits Draw(Listing_Standard l, bool overrides = false,
            SessionLimits? project = null, bool recipe = false)
        {
            _overrides = overrides;
            DrawField(l, "Memory (MiB)", "limits.memory", _memory, project?.MemoryMb, project.HasValue, recipe);
            DrawField(l, "Max processes and threads", "limits.pids", _pids, project?.Pids, project.HasValue, recipe);
            DrawField(l, "Open files per process", "limits.nofile", _nofile, project?.Nofile, project.HasValue, recipe);
            DrawField(l, "CPU (% of one core)", "limits.cpu", _cpu, project?.CpuPct, project.HasValue, recipe);
            return Values();
        }

        void DrawField(Listing_Standard l, string label, string id, LimitField field,
            int? inherited, bool knownProject, bool recipe)
        {
            if (!_overrides)
            {
                l.Label(label);
                field.Text = UiControls.Field(l, id, field.Text);
                return;
            }

            string defaultLabel = recipe ? "Project default (destination project)"
                : "Project default: " + (knownProject ? inherited?.ToString() ?? "No configured cap" : "Choose a project");
            UiControls.Select(l, label, field.Custom ? "Custom" : defaultLabel, new[]
            {
                new SelectorOption("Reset to project default", () => field.Custom = false),
                new SelectorOption("Custom", () =>
                {
                    field.Custom = true;
                    if (string.IsNullOrWhiteSpace(field.Text)) field.Text = inherited?.ToString() ?? "";
                }),
            }, out _);
            if (field.Custom) field.Text = UiControls.Field(l, id, field.Text);
        }

        int? Get(LimitField field) => _overrides && !field.Custom ? null : field.Value;

        SessionLimits Values() => new SessionLimits
        {
            MemoryMb = Get(_memory),
            Pids = Get(_pids),
            Nofile = Get(_nofile),
            CpuPct = Get(_cpu)
        };

        public bool TrySave(out SessionLimits value, out string error)
        {
            value = Values();
            error = null;
            foreach (var field in new[] { _memory, _pids, _nofile, _cpu })
            {
                bool valid = _overrides
                    ? !field.Custom || field.Value.HasValue
                    : string.IsNullOrWhiteSpace(field.Text) || field.Value.HasValue;
                if (valid) continue;
                error = _overrides ? "Enter a positive whole number for each custom limit, or reset it to project default."
                    : "Resource limits must be positive whole numbers, or blank for no configured cap.";
                return false;
            }
            return true;
        }
    }
}
