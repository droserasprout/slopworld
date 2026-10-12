using System.IO;
using Verse;

namespace SlopWorld
{
    // Adapts the game profile and logging to portable settings persistence.
    static class ProfileSettings
    {
        public static ModSettings Load() => ModSettingsStore.Load(FilePath(),
            error => Log.Error("[SlopWorld] could not load TOML settings: " + error));

        public static void Write(this ModSettings settings) => ModSettingsStore.Write(FilePath(), settings);

        public static void FlushIfDue(this ModSettings settings) => settings.FlushIfDue(Write);

        static string FilePath()
        {
            string profile;
            try { profile = GenFilePaths.SaveDataFolderPath; }
            catch { profile = ""; }
            if (string.IsNullOrEmpty(profile)) return Path.Combine("Config", "SlopWorld.toml");
            return Path.Combine(profile, "Config", "SlopWorld.toml");
        }

    }
}
