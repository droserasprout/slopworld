using System;
using System.IO;

namespace SlopWorld
{
    // Shared local path policy for endpoint discovery and mod-owned preferences.
    // The launcher supplies resolved roots to the game on every platform.
    internal static class ApplicationPaths
    {
        internal static string ConfigRoot =>
            Root("SLOPD_CONFIG_ROOT", "XDG_CONFIG_HOME", ".config");
        internal static string DataRoot => Root("SLOPD_DATA", "XDG_DATA_HOME", ".local/share");
        internal static string LikesPath => Path.Combine(DataRoot, "jukebox.toml");

        static string Root(string variable, string xdg, string fallback)
        {
            var explicitRoot = Environment.GetEnvironmentVariable(variable);
            if (explicitRoot != null)
                return explicitRoot;
            var root = Environment.GetEnvironmentVariable(xdg);
            if (string.IsNullOrEmpty(root))
                root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    fallback
                );
            return Path.Combine(root, "slopworld");
        }
    }
}
