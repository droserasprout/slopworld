using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Describes catalog-based preference, not Unity's native per-glyph resolution.
    static class FontSelection
    {
        public static string FirstInstalled(
            IEnumerable<string> candidates,
            IEnumerable<string> installed
        )
        {
            var names = new HashSet<string>(installed, StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in candidates)
                if (names.Contains(candidate))
                    return candidate;
            return "";
        }

        public static string Label(string selected, string preferred)
        {
            if (!string.IsNullOrEmpty(selected))
                return selected;
            return string.IsNullOrEmpty(preferred) ? "Auto" : "Auto (prefers " + preferred + ")";
        }
    }
}
