using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The final sandbox answer for a project or agent. This is deliberately a client-side
    // readout of requested parameters: the daemon still decides which paths exist and which
    // protected paths it drops when it builds bwrap's argv.
    public sealed class SandboxPreviewData
    {
        public string Title = "";
        public string Subtitle = "";
        public string Network = "";
        public List<string> Presets = new List<string>();
        public List<string> Notes = new List<string>();
        public List<SandboxPreviewField> Fields = new List<SandboxPreviewField>();

        public static SandboxPreviewData ForProject(ProjectInfo project) =>
            Build("Project sandbox", project?.Name ?? "(new project)", project?.Dir ?? "",
                project, null, null);

        public static SandboxPreviewData ForAgent(SessionInfo session)
        {
            var hub = SessionHub.Instance;
            var project = hub.Project(session.Project);
            string commandName = string.IsNullOrEmpty(session.Command)
                ? session.CommandPreset : session.Command;
            CommandInfo command = null;
            if (string.IsNullOrEmpty(session.Cmd) || string.IsNullOrWhiteSpace(session.Cmd))
                command = hub.Command(commandName);
            // Older daemons did not repeat the resolved preset name. Matching the resolved
            // command keeps their default-agent preview useful when the command text is unique.
            if (command == null && string.IsNullOrWhiteSpace(session.Cmd) &&
                !string.IsNullOrEmpty(session.Agent))
                command = hub.Commands.FirstOrDefault(c => c.Cmd == session.Agent);

            var data = Build("Agent sandbox", session.Name, session.Dir, project, command, session);
            if (project == null && !string.IsNullOrEmpty(session.Project))
                data.Notes.Add($"Project '{session.Project}' is not available; only known " +
                               "sandbox presets are shown.");
            if (!string.IsNullOrEmpty(commandName) && command == null &&
                string.IsNullOrWhiteSpace(session.Cmd))
                data.Notes.Add($"Command preset '{commandName}' is not available in the current " +
                               "preset list.");
            return data;
        }

        static SandboxPreviewData Build(string kind, string name, string dir,
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
            NetworkMode effectiveNetwork = agent != null && agent.NetworkOverride.HasValue &&
                                           (project == null || NetworkModeText.Allowed(
                                               agent.NetworkOverride.Value, project.Network))
                ? agent.NetworkOverride.Value
                : project?.Network ?? agent?.Network ?? NetworkMode.Private;

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
            data.Notes.Add("Read-only paths that also appear as read-write are shown as " +
                           "read-write. Missing or protected paths are dropped by slopd.");

            data.Fields.Add(new SandboxPreviewField("Included sandbox presets", data.Presets));
            data.Fields.Add(new SandboxPreviewField("Network",
                new List<string> { data.Network }));
            data.Fields.Add(new SandboxPreviewField("Read-only binds", ro));
            data.Fields.Add(new SandboxPreviewField("Read-write binds", rw));
            data.Fields.Add(new SandboxPreviewField("Device binds",
                Merge(presets, p => p.Dev, null)));
            data.Fields.Add(new SandboxPreviewField("Private paths",
                Merge(presets, p => p.Private, null)));
            data.Fields.Add(new SandboxPreviewField("Seed paths",
                Merge(presets, p => p.Seed, null)));
            data.Fields.Add(new SandboxPreviewField("Skip paths",
                Merge(presets, p => p.Skip, null)));
            data.Fields.Add(new SandboxPreviewField("Shared files",
                Merge(presets, p => p.Shared, null)));
            data.Fields.Add(new SandboxPreviewField("Forwarded environment",
                Merge(presets, p => p.Env, null)));
            data.Fields.Add(new SandboxPreviewField("Set environment (final)",
                FinalEnvironment(presets)));
            return data;
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

    public sealed class SandboxPreviewField
    {
        public readonly string Label;
        public readonly List<string> Values;

        public SandboxPreviewField(string label, List<string> values)
        {
            Label = label;
            Values = values ?? new List<string>();
        }
    }

    public static class SandboxPreviewPanel
    {
        public static void Draw(Rect outer, ref SmoothScroll scroll, SandboxPreviewData data)
        {
            Slab.Box(outer, SlopWidgets.Well, SlopWidgets.Edge);
            var pad = outer.ContractedBy(SlopWidgets.GapS);
            var view = new Rect(0f, 0f, pad.width - SlopWidgets.ScrollbarW,
                Mathf.Max(Height(data, pad.width - SlopWidgets.ScrollbarW), pad.height));
            scroll.Begin(pad, view);

            float y = 0f;
            y = TextBlock(view.width, y, data.Title, SlopWidgets.Lead);
            y += SlopWidgets.GapXS;
            if (!string.IsNullOrEmpty(data.Subtitle))
            {
                y = TextBlock(view.width, y, data.Subtitle, SlopWidgets.Dim);
                y += SlopWidgets.GapXS;
            }
            if (data.Notes.Count > 0)
            {
                y = TextBlock(view.width, y, string.Join("\n", data.Notes.ToArray()),
                    SlopWidgets.Faint);
                y += SlopWidgets.GapS;
            }

            foreach (var field in data.Fields)
                y = Field(view.width, y, field);

            scroll.End();
        }

        static float Height(SandboxPreviewData data, float width)
        {
            float y = 0f;
            y += Text.CalcHeight(data.Title, width) + SlopWidgets.GapXS;
            if (!string.IsNullOrEmpty(data.Subtitle))
                y += Text.CalcHeight(data.Subtitle, width) + SlopWidgets.GapXS;
            if (data.Notes.Count > 0)
                y += Text.CalcHeight(string.Join("\n", data.Notes.ToArray()), width) +
                     SlopWidgets.GapS;
            foreach (var field in data.Fields)
            {
                string text = field.Values.Count == 0
                    ? "(nothing)"
                    : string.Join("\n", field.Values.ToArray());
                y += SlopWidgets.RowH + SlopWidgets.GapXS +
                     Text.CalcHeight(text, width) + SlopWidgets.GapM;
            }
            return y + SlopWidgets.GapM;
        }

        static float Field(float width, float y, SandboxPreviewField field)
        {
            SlopWidgets.SectionHeading(new Rect(0f, y, width, SlopWidgets.RowH), field.Label);
            y += SlopWidgets.RowH + SlopWidgets.GapXS;
            string text = field.Values.Count == 0
                ? "(nothing)"
                : string.Join("\n", field.Values.ToArray());
            GUI.color = field.Values.Count == 0 ? SlopWidgets.Faint : SlopWidgets.Name;
            Widgets.Label(new Rect(0f, y, width, Text.CalcHeight(text, width)), text);
            GUI.color = Color.white;
            return y + Text.CalcHeight(text, width) + SlopWidgets.GapM;
        }

        static float TextBlock(float width, float y, string text, Color color)
        {
            GUI.color = color;
            Widgets.Label(new Rect(0f, y, width, Text.CalcHeight(text, width)), text);
            GUI.color = Color.white;
            return y + Text.CalcHeight(text, width);
        }
    }
}
