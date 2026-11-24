using System.Text.Json;

namespace Waylith
{
    static class EngineApplication
    {
        static void BeginFrame(float delta, float time) { }

        static void EndFrame() => Check.Calls.Add("EndFrame");
    }
}

namespace SlopWorld
{
    class SessionInfo(string name, bool alive)
    {
        public string Name => name;
        public bool Alive => alive;
    }

    class SessionHub
    {
        public static readonly SessionHub Instance = new();
        public List<SessionInfo> Sessions { get; set; } = [];
        public int SessionsVersion { get; set; } = 1;

        public void Connect() { }

        public void Update() { }
    }

    static class WorkspaceWindow
    {
        public static void Open(string name) => Check.Opened = name;
    }

    static class StripOptions
    {
        public static void Hide() { }
    }

    static class ModOptions
    {
        public static void Install() { }
    }

    static class UiFont
    {
        public static void Apply() { }
    }

    static class TerminalFont
    {
        public static void Invalidate() => Check.Calls.Add("Invalidate");
    }

    static class DaemonClient
    {
        public static void PumpCompletions(Action<Exception> error) { }
    }

    class Settings
    {
        public static readonly Settings S = new();

        public void FlushIfDue() { }
    }
}

namespace BrowserAdapters
{
    public sealed partial class WorkspaceRuntime
    {
        internal object? entry;
    }

    static class BrowserFontBank
    {
        internal static bool AcceptFallback = true;

        internal static bool CompleteFallbacks(string json)
        {
            Check.Calls.Add("CompleteFallbacks");
            return AcceptFallback;
        }

        internal static string[] Paths() => [];
    }

    static class BrowserGui
    {
        internal static void FontsChanged() => Check.Calls.Add("FontsChanged");

        internal static void Install() { }
    }

    static class BrowserModKeys
    {
        internal static void Install() { }
    }

    static class BrowserWorkspaceDoors
    {
        internal static void Install() { }
    }

    static class BrowserCursor
    {
        internal static JsonElement Batch;

        internal static void Update(JsonElement events) => Batch = events;

        internal static void Install() { }
    }

    static class WorkspaceDrawing
    {
        internal static void RequireEntry() { }

        internal static void BeginFrame(double elapsed) { }

        internal static void Update() { }

        internal static void Initialize() { }

        internal static string Frame(JsonElement items, double retained, bool initial)
        {
            if (initial && items.GetArrayLength() == 0)
                return "[\"chrome\"]";
            if (!items.Equals(BrowserCursor.Batch))
                throw new Exception("Cursor and GUI batches differ");
            return items.GetRawText();
        }
    }
}
