using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Builds the read-only sandbox projection. It knows about daemon data and inheritance;
    // SandboxPreviewPanel only renders the resulting rows.
    static class SandboxPreviewBuilder
    {
        public static SandboxPreviewData Build(string kind, string name, string dir,
                                                ProjectInfo project, CommandInfo command,
                                                SessionInfo agent)
        {
            var hub = SessionHub.Instance;
            var names = ResolveNames(command, project, agent, hub.Presets);
            var presets = names
                .Select(n => hub.Presets.FirstOrDefault(p => p.Name == n))
                .Where(p => p != null)
                .ToList();

            var ro = Merge(presets, p => p.Ro, null);
            var rw = Merge(presets, p => p.Rw, null);
            // bwrap emits read-only binds before read-write binds, so a duplicate is writable
            // in the final namespace and belongs only in the latter column here.
            ro.RemoveAll(path => rw.Contains(path));
            NetworkMode effectiveNetwork = agent != null && agent.NetworkOverride.HasValue
                ? agent.NetworkOverride.Value
                : project?.Network ?? agent?.Network ?? NetworkMode.Private;
            DnsConfig effectiveDns = agent?.DnsOverride ?? project?.Dns ??
                                     agent?.Dns ?? DnsConfig.Resolved();

            var data = new SandboxPreviewData
            {
                Title = $"{kind}: {name}",
                Subtitle = string.IsNullOrEmpty(dir) ? "" : $"Directory: {dir}",
                Network = NetworkModeText.Label(effectiveNetwork),
                Presets = names.Select(n => hub.Presets.Any(p => p.Name == n)
                        ? n : n + " (missing)").ToList(),
            };

            if (project != null && !string.IsNullOrEmpty(project.Name))
                data.Notes.Add($"Project: {project.Name}");
            if (command != null)
                data.Notes.Add($"Command preset: {command.Name}");
            else if (agent != null && !string.IsNullOrWhiteSpace(agent.Cmd))
                data.Notes.Add("Command: this agent's own command line");
            if (agent?.NetworkOverride is NetworkMode overrideMode)
                data.Notes.Add("Agent override: " + NetworkModeText.ShortLabel(overrideMode));
            data.Notes.Add("DNS: " + effectiveDns.Label);
            data.Notes.Add("Read-only paths that also appear as read-write are shown as " +
                           "read-write. Missing or protected paths are dropped by slopd.");

            data.Fields.Add(new SandboxPreviewField("Included sandbox presets", data.Presets));
            data.Fields.Add(new SandboxPreviewField("Network",
                new List<string> { data.Network }));
            data.Fields.Add(new SandboxPreviewField("DNS",
                new List<string> { effectiveDns.Label }));
            if (agent != null)
                data.Fields.Add(new SandboxPreviewField("Joined breadcrumbs",
                    JoinedBreadcrumbs(project, agent, hub.Shortcuts, hub.Config)));
            if (agent != null)
                data.Fields.Add(new SandboxPreviewField("Resource limits",
                    LimitLines(agent.Limits)));
            data.Fields.Add(new SandboxPreviewField("Read-only binds", ro));
            data.Fields.Add(new SandboxPreviewField("Read-write binds", rw));

            var presetFields = new[]
            {
                new KeyValuePair<string, Func<PresetInfo, List<string>>>("Device binds",
                    p => p.Dev),
                new KeyValuePair<string, Func<PresetInfo, List<string>>>("Private paths",
                    p => p.Private),
                new KeyValuePair<string, Func<PresetInfo, List<string>>>("Seed paths",
                    p => p.Seed),
                new KeyValuePair<string, Func<PresetInfo, List<string>>>("Skip paths",
                    p => p.Skip),
                new KeyValuePair<string, Func<PresetInfo, List<string>>>("Shared files",
                    p => p.Shared),
                new KeyValuePair<string, Func<PresetInfo, List<string>>>("Forwarded environment",
                    p => p.Env),
            };
            foreach (var field in presetFields)
                data.Fields.Add(new SandboxPreviewField(field.Key,
                    Merge(presets, field.Value, null)));
            data.Fields.Add(new SandboxPreviewField("Set environment (final)",
                FinalEnvironment(presets)));
            return data;
        }

        // The daemon resolves named project attachments before named agent attachments, keeping
        // the first occurrence of a name. Show the resulting text block, including the generated
        // instructions discovery entry when it is enabled for this agent.
        static List<string> JoinedBreadcrumbs(ProjectInfo project, SessionInfo agent,
                                              List<ShortcutInfo> shortcuts, SlopConfig config)
        {
            var names = new List<string>();
            AddBreadcrumbNames(names, project?.Breadcrumbs);
            AddBreadcrumbNames(names, agent?.Breadcrumbs);

            var text = names
                .Select(name => shortcuts.FirstOrDefault(s => s.Name == name &&
                    s.Kind == ShortcutKind.Breadcrumb)?.Text)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToList();
            if (agent.SlopworldMd && agent.InstructionsBreadcrumb &&
                config.InstructionsBreadcrumbEnabled)
            {
                string discovery = config.RenderInstructionsBreadcrumb(project?.Name);
                if (!string.IsNullOrWhiteSpace(discovery)) text.Add(discovery.Trim());
            }
            return text.Count == 0
                ? new List<string>()
                : new List<string> { string.Join("\n", text.ToArray()) };
        }

        static void AddBreadcrumbNames(List<string> names, IEnumerable<string> attached)
        {
            if (attached == null) return;
            foreach (string name in attached)
                if (!names.Contains(name)) names.Add(name);
        }

        static List<string> LimitLines(SessionLimits limits)
        {
            var lines = new List<string>();
            if (limits.MemoryMb.HasValue) lines.Add($"Memory: {limits.MemoryMb.Value} MiB");
            if (limits.Pids.HasValue) lines.Add($"Max processes and threads: {limits.Pids.Value}");
            if (limits.Nofile.HasValue) lines.Add($"Open files per process: {limits.Nofile.Value}");
            if (limits.CpuPct.HasValue) lines.Add($"CPU: {limits.CpuPct.Value}% of one core");
            return lines;
        }

        static List<string> ResolveNames(CommandInfo command, ProjectInfo project,
                                         SessionInfo agent, List<PresetInfo> available)
        {
            var asked = new List<string> { "global" };
            if (command != null) asked.AddRange(command.Sandbox);
            if (project != null) asked.AddRange(project.Sandbox);
            if (agent != null) asked.AddRange(agent.Sandbox);

            var result = new List<string>();
            var visiting = new HashSet<string>();
            foreach (string name in asked)
                AddPreset(name, available, result, visiting);
            return result;
        }

        static void AddPreset(string name, List<PresetInfo> available, List<string> result,
                              HashSet<string> visiting)
        {
            if (result.Contains(name)) return;
            if (!visiting.Add(name)) return;

            var preset = available.FirstOrDefault(p => p.Name == name);
            if (preset != null)
                foreach (string required in preset.Requires)
                    AddPreset(required, available, result, visiting);

            visiting.Remove(name);
            if (!result.Contains(name)) result.Add(name);
        }

        static List<string> Merge(List<PresetInfo> presets,
                                  Func<PresetInfo, List<string>> pick,
                                  IEnumerable<string> own)
        {
            var result = new List<string>();
            foreach (var preset in presets)
                AddUnique(result, pick(preset));
            AddUnique(result, own);
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        static void AddUnique(List<string> result, IEnumerable<string> values)
        {
            if (values == null) return;
            foreach (string value in values)
                if (!string.IsNullOrEmpty(value) && !result.Contains(value))
                    result.Add(value);
        }

        static List<string> FinalEnvironment(List<PresetInfo> presets)
        {
            var values = new Dictionary<string, string>();
            var order = new List<string>();
            foreach (var preset in presets)
                foreach (var pair in preset.Setenv)
                    SetEnvironment(values, order, pair.Key, pair.Value);

            return order.Select(key => key + "=" + values[key]).ToList();
        }

        static void SetEnvironment(Dictionary<string, string> values, List<string> order,
                                   string key, string value)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) return;
            if (!values.ContainsKey(key)) order.Add(key);
            values[key] = value ?? "";
        }
    }
}
