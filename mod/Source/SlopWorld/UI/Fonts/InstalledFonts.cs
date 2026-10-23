using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Owns OS discovery. Consumers use Revision to invalidate fonts, probes, and menus.
    static class InstalledFonts
    {
        static IReadOnlyList<string> _all;
        public static int Revision { get; private set; }
        public static IReadOnlyList<string> All => _all ?? (_all = Scan());

        public static void Rescan()
        {
            _all = null;
            Revision++;
        }

        static IReadOnlyList<string> Scan()
        {
            var found = new List<string>();
            try
            {
                var names = Font.GetOSInstalledFontNames();
                if (names == null)
                    return found.AsReadOnly();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in names)
                    if (!string.IsNullOrEmpty(name) && seen.Add(name))
                        found.Add(name);
                found.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                Log.Warning($"[SlopWorld] installed font scan: {e.Message}");
            }
            return found.AsReadOnly();
        }
    }
}
