using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Orders installed Nerd Font variants within each candidate family.
    // TerminalFont creates fonts; InstalledFonts owns discovery.
    static class TerminalFontFallbacks
    {
        public static string[] Build(
            IEnumerable<string> candidates,
            IEnumerable<string> installed,
            string selected = null
        )
        {
            var patched = new List<string>();
            foreach (string name in installed)
                if (
                    !string.IsNullOrEmpty(name)
                    && FontFamilyIdentity.NerdMarker(name) >= 0
                    && !IsProportional(name)
                )
                    patched.Add(name);
            patched.Sort(
                (a, b) =>
                {
                    int width = IsMono(b).CompareTo(IsMono(a));
                    if (width != 0)
                        return width;
                    int family = StringComparer.OrdinalIgnoreCase.Compare(a, b);
                    return family != 0 ? family : StringComparer.Ordinal.Compare(a, b);
                }
            );

            var chain = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(selected))
                Add(selected, chain, seen);
            foreach (string candidate in candidates)
            {
                string family = FontFamilyIdentity.Key(candidate);
                foreach (string name in patched)
                    if (FontFamilyIdentity.Key(name) == family)
                        Add(name, chain, seen);
                Add(candidate, chain, seen);
            }
            return chain.ToArray();
        }

        static void Add(string name, List<string> chain, HashSet<string> seen)
        {
            if (seen.Add(name))
                chain.Add(name);
        }

        static bool IsMono(string name) =>
            name.EndsWith(" Mono", StringComparison.OrdinalIgnoreCase);

        static bool IsProportional(string name) =>
            name.EndsWith(" Propo", StringComparison.OrdinalIgnoreCase);
    }
}
