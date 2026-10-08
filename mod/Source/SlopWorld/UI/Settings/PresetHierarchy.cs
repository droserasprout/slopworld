using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // One catalog group forms a forest. Multi-dependency bundles stay at the root;
    // a dependency in another group is described in the editor, not a phantom row.
    internal sealed class PresetHierarchy
    {
        public readonly List<PresetInfo> Items = new List<PresetInfo>();
        readonly Dictionary<PresetInfo, int> _depths = new Dictionary<PresetInfo, int>();

        public PresetHierarchy(IEnumerable<PresetInfo> presets)
        {
            var sorted = presets.OrderBy(p => p.Name == "global" ? 0 : 1)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var parents = sorted.ToDictionary(p => p, p => Parent(p, sorted));
            foreach (var preset in sorted.Where(p => parents[p] == null))
                Append(preset, 0, sorted, parents);
        }

        public int Depth(PresetInfo preset) => _depths[preset];

        static PresetInfo DirectParent(PresetInfo preset, List<PresetInfo> group) =>
            preset.Name != "global" && preset.Requires.Count == 1
                ? group.FirstOrDefault(p => p.Name == preset.Requires[0]) : null;

        static PresetInfo Parent(PresetInfo preset, List<PresetInfo> group)
        {
            var parent = DirectParent(preset, group);
            var seen = new HashSet<PresetInfo> { preset };
            for (var ancestor = parent; ancestor != null; ancestor = DirectParent(ancestor, group))
                if (!seen.Add(ancestor)) return null;
            return parent;
        }

        void Append(PresetInfo preset, int depth, List<PresetInfo> sorted,
                    Dictionary<PresetInfo, PresetInfo> parents)
        {
            Items.Add(preset);
            _depths[preset] = depth;
            foreach (var child in sorted.Where(p => parents[p] == preset))
                Append(child, depth + 1, sorted, parents);
        }
    }
}
