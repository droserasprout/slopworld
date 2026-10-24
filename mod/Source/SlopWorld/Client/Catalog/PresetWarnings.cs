using System;
using System.Collections.Generic;
using System.Linq;
using WarningValues = SlopWorld.WireProtocol.PresetWarningLevel;

namespace SlopWorld
{
    public enum PresetWarningLevel
    {
        None,
        Sensitive,
        HostControl,
    }

    // Descriptive access warnings, independent of daemon authorization. Resolve the supplied
    // catalog so saved template snapshots and live preset edits use their own dependencies.
    public sealed class PresetWarning
    {
        public PresetWarningLevel Level;
        public string Details = "";
        public string Sources = "";
        public string Label => PresetWarnings.Label(Level);
        public string Summary => Level == PresetWarningLevel.None ? "" : Label + " via " + Sources;
    }

    public static class PresetWarnings
    {
        public static string Label(PresetWarningLevel level) =>
            level == PresetWarningLevel.HostControl ? "Host control"
            : level == PresetWarningLevel.Sensitive ? "Sensitive access"
            : "No declared warning";

        public static PresetWarningLevel OwnLevel(PresetInfo preset)
        {
            if (
                preset.Tmux
                || preset.DaemonConfig
                || preset.WarningLevel == WarningValues.HostControl
            )
                return PresetWarningLevel.HostControl;
            // Unrecognized severity must not silently suppress an access warning.
            if (
                !string.IsNullOrEmpty(preset.WarningLevel)
                && preset.WarningLevel != WarningValues.None
                && preset.WarningLevel != WarningValues.Sensitive
            )
                return PresetWarningLevel.HostControl;
            return preset.WarningLevel == WarningValues.Sensitive
                ? PresetWarningLevel.Sensitive
                : PresetWarningLevel.None;
        }

        public static PresetWarning For(PresetInfo preset, IEnumerable<PresetInfo> catalog) =>
            Resolve(new[] { preset }, catalog);

        public static PresetWarning ForSelection(
            IEnumerable<string> names,
            IEnumerable<PresetInfo> catalog
        )
        {
            var byName = Catalog(catalog);
            return Resolve(
                names.Distinct().Where(byName.ContainsKey).Select(n => byName[n]),
                byName.Values
            );
        }

        static Dictionary<string, PresetInfo> Catalog(IEnumerable<PresetInfo> catalog)
        {
            var result = new Dictionary<string, PresetInfo>(StringComparer.Ordinal);
            foreach (var preset in catalog)
                result[preset.Name] = preset;
            return result;
        }

        static PresetWarning Resolve(IEnumerable<PresetInfo> roots, IEnumerable<PresetInfo> catalog)
        {
            var byName = Catalog(catalog);
            var queue = new Queue<PresetInfo>(roots);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var warnings = new List<(string Name, PresetWarningLevel Level, string Text)>();
            while (queue.Count > 0)
            {
                var preset = queue.Dequeue();
                if (!visited.Add(preset.Name))
                    continue;
                var level = OwnLevel(preset);
                if (level != PresetWarningLevel.None)
                {
                    var reasons = new List<string>();
                    if (!string.IsNullOrWhiteSpace(preset.Escapes))
                        reasons.Add(preset.Escapes.Trim().TrimEnd('.'));
                    if (preset.Tmux)
                        reasons.Add("Can control host terminals through the daemon's tmux socket");
                    if (preset.DaemonConfig)
                        reasons.Add("Can read and use the daemon root credential");
                    if (reasons.Count == 0)
                        reasons.Add("Access declared by this preset");
                    warnings.Add((preset.Name, level, string.Join(". ", reasons) + "."));
                }
                foreach (var name in preset.Requires)
                    if (byName.TryGetValue(name, out var dependency))
                        queue.Enqueue(dependency);
            }
            var result = new PresetWarning();
            if (warnings.Count == 0)
                return result;
            result.Level = warnings.Max(w => w.Level);
            result.Sources = string.Join(
                ", ",
                warnings.Where(w => w.Level == result.Level).Select(w => w.Name)
            );
            result.Details = string.Join(
                "\n",
                warnings.Select(w => Label(w.Level) + " via " + w.Name + ": " + w.Text)
            );
            return result;
        }
    }
}
