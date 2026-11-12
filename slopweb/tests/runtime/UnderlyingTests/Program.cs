using BrowserAdapters;
using UnityEngine;
using Verse;

static class Check
{
    public static readonly List<string> Calls = [];

    static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    public static void Main()
    {
        Event.current.type = EventType.Repaint;
        Find.WindowStack.Windows.Add(new SlopWorld.WorkspaceWindow());
        BrowserUnderlyingWorkspace.Draw();
        Expect(
            !BrowserUnderlyingWorkspace.BackgroundVisible() && Calls.Count == 0,
            "Covered layer drew"
        );
        Find.WindowStack.Windows.Clear();
        BrowserUnderlyingWorkspace.Draw();
        Expect(Calls.SequenceEqual(["top:True", "sidebar:True", "grid:218"]), "Native owner order");
        Expect(GUI.Groups == 0 && GUI.enabled, "Drawing leaked GUI state");
        Calls.Clear();
        Find.WindowStack.Input = false;
        Event.current.type = EventType.KeyDown;
        BrowserUnderlyingWorkspace.Draw();
        Expect(Calls.Count == 0, "Modal leaked action hotkey");
        Event.current.type = EventType.Repaint;
        BrowserUnderlyingWorkspace.Draw();
        Expect(
            Calls.SequenceEqual(["top:False", "sidebar:False", "grid:218"]),
            "Modal obscured chrome"
        );
        Expect(!GizmoGridDrawer.Enabled, "Modal left actions enabled");
        Calls.Clear();
        Find.WindowStack.Input = true;
        SlopWorld.SessionSelectable.CurrentObject = null;
        BrowserUnderlyingWorkspace.Draw();
        Expect(
            Calls.SequenceEqual(["top:True", "sidebar:True"]),
            "Missing session drew stale actions"
        );
        SlopWorld.SessionSelectable.CurrentObject = new SlopWorld.SessionSelectable();
        SlopWorld.TopBar.OpenWindow = true;
        Calls.Clear();
        BrowserUnderlyingWorkspace.Draw();
        Expect(Calls.SequenceEqual(["top:True"]), "Door transition drew stale chrome");
        SlopWorld.TopBar.OpenWindow = false;
        Find.WindowStack.Windows.Clear();
        GizmoGridDrawer.Throw = true;
        try
        {
            BrowserUnderlyingWorkspace.Draw();
        }
        catch (InvalidOperationException) { }
        Expect(GUI.Groups == 0 && GUI.enabled, "Failed grid leaked GUI state");
        Calls.Clear();
        SlopWorld.WorkspaceVisibility.Hidden = true;
        BrowserUnderlyingWorkspace.Draw();
        Expect(Calls.Count == 0, "Hidden chrome drew");
        WorkspaceDrawing.Entry = false;
        bool rejected = false;
        try
        {
            BrowserUnderlyingWorkspace.BackgroundVisible();
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Expect(rejected, "Colony boundary was not checked");
        Console.WriteLine(
            "Underlying workspace: coverage, modal input, stale selection/door transition, cleanup and Entry guards passed."
        );
    }
}

namespace BrowserAdapters
{
    public static class WorkspaceDrawing
    {
        public static bool Entry = true;

        public static void RequireEntry()
        {
            if (!Entry)
                throw new InvalidOperationException();
        }
    }

    public static class BrowserSidebar
    {
        public static void Draw(bool interactive) => Check.Calls.Add("sidebar:" + interactive);
    }
}

namespace UnityEngine
{
    public struct Rect(float x, float y, float width, float height)
    {
        public float x = x,
            y = y,
            width = width,
            height = height;
    }

    public enum EventType
    {
        Repaint,
        Layout,
        Used,
        KeyDown,
    }

    public class Event
    {
        public static Event current = new();
        public EventType type;
    }

    public static class GUI
    {
        public static bool enabled = true;
        public static int Groups;

        public static void BeginGroup(Rect rect) => Groups++;

        public static void EndGroup() => Groups--;
    }
}

namespace Verse
{
    public class Window { }

    public class WindowStack
    {
        public List<Window> Windows = [];
        public bool Input = true;

        public bool GetsInput(Window? window) => Input;
    }

    public static class Find
    {
        public static WindowStack WindowStack = new();
    }

    public static class UI
    {
        public static int screenWidth = 1280,
            screenHeight = 720;
    }

    public interface ISelectable
    {
        IEnumerable<Gizmo> GetGizmos();
    }

    public class Gizmo
    {
        public float GetWidth(float width) => 75f;
    }

    public class Command : Gizmo
    {
        public string defaultLabel = "Terminal";
    }

    public static class GizmoGridDrawer
    {
        public static bool Enabled,
            Throw;

        public static void DrawGizmoGrid(
            IEnumerable<Gizmo> gizmos,
            float startX,
            out Gizmo? hovered
        )
        {
            Enabled = GUI.enabled;
            Check.Calls.Add("grid:" + startX);
            if (Throw)
                throw new InvalidOperationException();
            hovered = null;
        }
    }
}

namespace SlopWorld
{
    public class WorkspaceWindow : Window { }

    public class SessionSelectable : ISelectable
    {
        public static ISelectable? CurrentObject { get; set; } = new SessionSelectable();
        public static string Current => "fixture";

        public IEnumerable<Gizmo> GetGizmos() => [new Command()];
    }

    public class Geometry
    {
        public Rect Content = new(210, 26, 1070, 694);
    }

    public static class WorkspaceLayout
    {
        public static Geometry Current => new();
    }

    public static class WorkspaceVisibility
    {
        public static bool Shown => true;
        public static bool Hidden { get; set; }
    }

    public static class Sidebar
    {
        public static Rect AddBar => new(0, 690, 210, 30);
    }

    public static class UiTheme
    {
        public static float GapM => 8;
    }

    public static class SessionGizmoInput
    {
        public const float ActionRowHeight = 75;
    }

    public static class TopBar
    {
        public static bool OpenWindow;

        public static void Draw(bool interactive)
        {
            Check.Calls.Add("top:" + interactive);
            if (OpenWindow)
                Find.WindowStack.Windows.Add(new WorkspaceWindow());
        }
    }
}
