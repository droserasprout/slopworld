using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Name suggestions shared by the game-bound dialogs and kept pure so their suffix rules
    // can be checked without a live RimWorld process.
    public static class NameTools
    {
        public static string FreeName(string name, IEnumerable<string> taken, string fallback)
        {
            string stem = name ?? "";
            int suffixAt = stem.Length;
            while (suffixAt > 0 && char.IsDigit(stem[suffixAt - 1])) suffixAt--;

            // A number embedded in a name is part of the name. Only remove a number that
            // already looks like the counter this method appends.
            if (suffixAt > 0 && suffixAt < stem.Length && IsNameSeparator(stem[suffixAt - 1]))
                stem = stem.Substring(0, suffixAt - 1);
            stem = stem.TrimEnd(' ', '-', '_');
            if (stem.Length == 0) stem = name ?? fallback;

            var used = taken.ToList();
            for (int n = 2; n <= 99; n++)
            {
                string candidate = stem + "-" + n;
                if (!used.Contains(candidate)) return candidate;
            }
            return stem;
        }

        static bool IsNameSeparator(char c) => c == ' ' || c == '-' || c == '_';
    }
}
