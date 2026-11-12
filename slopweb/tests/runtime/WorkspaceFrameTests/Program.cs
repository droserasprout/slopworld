// Run the actual workspace traversal and input dispatcher without an engine.
using BrowserAdapters;
using UnityEngine;
using Verse;

static class Check
{
    internal static readonly List<string> Calls = [];
    internal static int PendingTips;

    static void Main()
    {
        UnityStubs.Gui.GuiBackend.Current = BrowserGui.Install();
        GUI.OnDispatchInput = WorkspaceGuiCalls.Input;
        GUI.OnProcessWindows = WorkspaceGuiCalls.Deferred;
        foreach (string batch in new[] { "[{\"kind\":\"move\",\"x\":45,\"y\":65}]", "[]", "[]" })
        {
            Calls.Clear();
            WorkspaceDrawing.Frame(batch);
            string[] passes =
                batch == "[]" ? ["Layout", "Repaint"] : ["MouseMove", "Layout", "Repaint"];
            var expected = passes.SelectMany(pass =>
                new[]
                {
                    pass + ":input",
                    pass + ":chrome",
                    pass + ":tooltips",
                    pass + ":windows",
                    pass + ":deferred",
                    pass + ":messages",
                }
            );
            if (!Calls.SequenceEqual(expected))
                throw new Exception("Workspace foreground order: " + string.Join(", ", Calls));
            if (PendingTips != 0)
                throw new Exception("Tooltip window missed deferred dispatch");
        }
        Console.WriteLine(
            "Workspace hover: native tooltip pass precedes deferred windows; stationary pointer survives idle repaints."
        );
    }

    internal static void Record(string stage)
    {
        if (Event.current.mousePosition.x != 45 || Event.current.mousePosition.y != 65)
            throw new Exception("Workspace lost hover coordinates");
        Calls.Add(Event.current.type + ":" + stage);
    }
}

namespace UnityEngine
{
    public static class Screen
    {
        public static int width = 1280,
            height = 720;
    }

    public static class Time
    {
        public static float unscaledTime;
        public static int frameCount;
    }

    public class RenderTexture
    {
        public bool IsCreated() => false;
    }

    public static class WorkspaceGuiCalls
    {
        public static void Input() => Check.Record("input");

        public static void Deferred()
        {
            Check.Record("deferred");
            Check.PendingTips = 0;
        }
    }
}

namespace Verse
{
    public enum ProgramState
    {
        Entry,
        Playing,
    }

    public static class Current
    {
        public static ProgramState ProgramState;
        public static object? Game;
    }

    public static class Prefs
    {
        public static float UIScale;
    }

    public static class UI
    {
        public static int screenWidth = 1280,
            screenHeight = 720;

        public static void ApplyUIScale() { }
    }

    public class Window
    {
        public virtual void DoWindowContents(Rect rect) { }

        public void Close(bool sound) { }
    }

    public class Dialog_MessageBox : Window
    {
        public string text = "";
    }

    public class WindowStack
    {
        public List<Window> Windows = [];

        public void WindowsUpdate() { }

        public void WindowStackOnGUI()
        {
            Check.Record("windows");
            if (Event.current.type == EventType.Repaint && Check.PendingTips != 1)
                throw new Exception(
                    "Tooltip did not enqueue its immediate window before traversal"
                );
        }
    }

    public static class Find
    {
        public static WindowStack WindowStack = new();
    }

    public static class Messages
    {
        public static void Update() { }

        public static void MessagesDoGUI() => Check.Record("messages");
    }

    public static class TooltipHandler
    {
        public static void DoTooltipGUI()
        {
            Check.Record("tooltips");
            if (Event.current.type == EventType.Repaint)
                Check.PendingTips++;
        }
    }

    public static class Translation
    {
        public static string Translate(this string value) => value;
    }
}

namespace BrowserAdapters
{
    public static class BrowserPortraits
    {
        public static void Bind() { }
    }

    public static class BrowserFontBank
    {
        public static void ConfigureDensity(float scale) { }
    }

    public static class GameSession
    {
        public static void ConfigureViewport(int width, int height) { }

        public static string Snapshot() => "{}";
    }

    public static class BrowserWorkspaceDoors
    {
        public static void Update() { }

        public static object Snapshot() => new();
    }

    public static class BrowserSidebar
    {
        public static object Snapshot() => new();
    }

    public static class BrowserUnderlyingWorkspace
    {
        public static object Snapshot() => new();

        public static void Draw() => Check.Record("chrome");
    }

    public static class RuntimeServices
    {
        public static string[] Errors = [];
    }
}

namespace Waylith.UnityCompat
{
    public class UnityRuntime
    {
        public static UnityRuntime? Current = new();

        public void BeginFrame(float delta, float elapsed) { }
    }
}
