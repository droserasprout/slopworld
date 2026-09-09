namespace Verse
{
    static class GenFilePaths { public static string SaveDataFolderPath = ""; }
    static class Log { public static void Error(string message) { } }
    static class UI { public static float screenWidth, screenHeight; }
}

namespace SlopWorld
{
    static class Cutscene { public static bool Playing; }
    static class TopBar { public static float H => 26f; }
    static class UiMetrics { public static int Revision => 0; }

    sealed class ModEntry
    {
        public static readonly ModEntry Instance = new ModEntry();
        public readonly ModSettings settings = new ModSettings();
    }
}
