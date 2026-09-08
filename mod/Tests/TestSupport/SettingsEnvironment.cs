namespace Verse
{
    static class GenFilePaths { public static string SaveDataFolderPath = ""; }
    static class Log { public static void Error(string message) { } }
}

namespace SlopWorld
{
    sealed class ModEntry
    {
        public static readonly ModEntry Instance = new ModEntry();
        public readonly ModSettings settings = new ModSettings();
    }
}
