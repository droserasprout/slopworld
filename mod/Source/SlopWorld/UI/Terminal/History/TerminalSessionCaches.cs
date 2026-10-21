using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Retained pane state belongs only to sessions still in the authoritative inventory.
    internal static class TerminalSessionCaches
    {
        internal static void Prune<T>(Dictionary<string, T> cache, Func<string, bool> exists)
        {
            List<string> gone = null;
            foreach (var name in cache.Keys)
                if (!exists(name))
                    (gone ?? (gone = new List<string>())).Add(name);
            if (gone == null)
                return;
            foreach (var name in gone)
                cache.Remove(name);
        }
    }
}
