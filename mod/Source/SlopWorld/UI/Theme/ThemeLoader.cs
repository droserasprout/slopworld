using System;
using System.Collections.Generic;
using Verse;

namespace SlopWorld
{
    // Shared runtime loading and recovery. Theme owners retain their caches, record
    // conversion, and fallback palettes; ThemeCatalog owns parsing and validation.
    internal static class ThemeLoader
    {
        public static List<T> Load<T>(string kind, Func<ThemeCatalog, List<T>> convert,
            Func<T> fallback)
        {
            try
            {
                string root = ModEntry.Instance?.Content?.RootDir;
                return convert(ThemeCatalog.Load(root));
            }
            catch (Exception e)
            {
                // Builds validate shipped catalogs. A fallback also supports a manually
                // copied DLL whose content directory was omitted.
                Log.Error("[SlopWorld] could not load " + kind + " theme catalog: " + e);
                return new List<T> { fallback() };
            }
        }
    }
}
