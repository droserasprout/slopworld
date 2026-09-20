namespace Verse
{
    static class GenFilePaths { public static string SaveDataFolderPath = ""; }
    static class Log
    {
        public static void Message(string message) { }
        public static void Error(string message) { }
        public static void Warning(string message) { }
    }
    static class UI { public static float screenWidth = 0f, screenHeight = 0f; }
    public enum GameFont { Tiny, Small, Medium }
    static partial class Text
    {
        public static bool TinyFontSupported = true;
        public static float LineHeightOf(GameFont font) =>
            font == GameFont.Tiny ? 11f : font == GameFont.Medium ? 15f : 13f;
    }
    static class Prefs { public static float UIScale = 1f; }
}

namespace SlopWorld
{
    static class Cutscene { public static bool Playing = false; }
    static class TopBar { public static float H => 26f; }
    static class UiFont { public static int RevisionValue; public static int Revision => RevisionValue; }
    static partial class UiTheme { public static int AtlasRevisionValue; public static int AtlasRevision => AtlasRevisionValue; }

    sealed class ModEntry
    {
        public static readonly ModEntry Instance = new ModEntry();
        public readonly ModSettings settings = new ModSettings();
    }
}
