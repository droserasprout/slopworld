using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Shares family keys between menus and terminal fallbacks.
    // Font creation and persistence retain installed names.
    static class FontFamilyIdentity
    {
        public static int NerdMarker(string name) =>
            name.IndexOf(" Nerd Font", StringComparison.OrdinalIgnoreCase);

        public static string MenuGroup(string name, IEnumerable<string> installed)
        {
            int marker = NerdMarker(name);
            if (marker >= 0)
            {
                string key = Key(name);
                // Group renamed or compacted Nerd Font names under the installed original.
                foreach (string original in installed)
                    if (
                        !string.IsNullOrEmpty(original)
                        && NerdMarker(original) < 0
                        && Key(original) == key
                    )
                        return LeadingWord(original);
                name = name.Substring(0, marker);
            }
            return LeadingWord(name);
        }

        static string LeadingWord(string name)
        {
            int space = name.IndexOf(' ');
            return space > 0 ? name.Substring(0, space) : name;
        }

        public static string Key(string name)
        {
            int marker = NerdMarker(name);
            if (marker >= 0)
                name = name.Substring(0, marker);
            string key = name.Replace(" ", "").Replace("-", "").ToLowerInvariant();
            // Reserved-name replacements and abbreviations need explicit aliases.
            switch (key)
            {
                case "caskaydiamono":
                    return "cascadiamono";
                case "caskaydiacove":
                    return "cascadiacode";
                case "saucecodepro":
                    return "sourcecodepro";
                case "literationmono":
                    return "liberationmono";
                case "notosansm":
                    return "notosansmono";
                case "dejavusansm":
                    return "dejavusansmono";
                default:
                    return key;
            }
        }
    }
}
