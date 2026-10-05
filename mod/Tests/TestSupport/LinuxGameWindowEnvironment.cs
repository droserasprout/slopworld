namespace UnityEngine
{
    public enum RuntimePlatform { LinuxPlayer, WindowsPlayer }
    public enum FullScreenMode { Windowed }

    public static partial class Application
    {
        public static RuntimePlatform platform = RuntimePlatform.WindowsPlayer;
    }

    public sealed class Display
    {
        public static readonly Display main = new Display();
        public int systemWidth = 1920;
        public int systemHeight = 1080;
    }

    public static class Screen
    {
        public static int width = 1920;
        public static int height = 1080;
        public static void SetResolution(int w, int h, FullScreenMode mode) { }
    }
}

namespace Verse
{
    public static class LongEventHandler
    {
        public static bool AnyEventNowOrWaiting;
    }
}

namespace SlopWorld
{
    public static class NextPlanet
    {
        public static bool Pending;
    }
}
