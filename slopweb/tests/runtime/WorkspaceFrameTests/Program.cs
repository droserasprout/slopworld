// Run the actual workspace traversal and input dispatcher without an engine.
using System.Text.Json;
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
        WorkspaceDrawing.ConfigureViewport(1280, 720, 5f / 3);
        WorkspaceDrawing.ConfigureViewport(1600, 900, 2);
        if (Prefs.UIScale != 2 || BrowserFontBank.Density != 2)
            throw new Exception("Default scale did not follow display density");
        Prefs.UIScale = 1.25f;
        WorkspaceDrawing.ConfigureViewport(1400, 800, 2);
        WorkspaceDrawing.ConfigureViewport(1400, 800, 1.25f);
        WorkspaceDrawing.ConfigureViewport(1280, 720, 1.5f);
        if (Prefs.UIScale != 1.25f || BrowserFontBank.Density != 1.25f)
            throw new Exception("Viewport resize replaced the selected UI scale");
        foreach (string batch in new[] { "[{\"kind\":\"move\",\"x\":45,\"y\":65}]", "[]", "[]" })
        {
            Calls.Clear();
            using var events = JsonDocument.Parse(batch);
            WorkspaceDrawing.Frame(events.RootElement, -1, false);
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
        // A slider changes Prefs during input; layout/repaint in the same frame
        // must measure using the new glyph density, including repeated changes.
        foreach (float scale in new[] { 1.55f, 2f, 0.75f, 1.55f })
        {
            GUI.OnDispatchInput = () =>
            {
                WorkspaceGuiCalls.Input();
                if (Event.current.type == EventType.MouseMove)
                    Prefs.UIScale = scale;
            };
            using var events = JsonDocument.Parse("[{\"kind\":\"move\",\"x\":45,\"y\":65}]");
            WorkspaceDrawing.Frame(events.RootElement, -1, false);
            if (BrowserFontBank.Density != scale)
                throw new Exception("Live scale did not reach the rasterizer");
        }
        Console.WriteLine(
            "Workspace hover: native tooltip pass precedes deferred windows; stationary pointer survives idle repaints."
        );
    }

    internal static void Record(string stage)
    {
        if (
            Event.current.type is EventType.Layout or EventType.Repaint
            && BrowserFontBank.Density != Prefs.UIScale
        )
            throw new Exception("Layout/repaint used stale glyph density");
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
    public static class BrowserFontBank
    {
        public static float Density;

        public static void ConfigureDensity(float scale) => Density = scale;
    }

    public static class WorkspaceScene
    {
        public static void ConfigureViewport(int width, int height) { }

        public static object Status() => new { };

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

namespace BrowserAdapters
{
    public static partial class WorkspaceDrawing
    {
        static void BindModServices()
        {
            shortcuts = applyLayout = flushTerminalInput = drawUiBubble = null;
            terminalDeadline = cursorDeadline = null;
        }
    }
}
