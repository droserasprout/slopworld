// Game-free GUI surface: tests execute the real browser dispatcher, recording its
// native event passes and serialization boundary without initializing the engine.
namespace UnityEngine
{
    public enum EventType
    {
        MouseDown,
        MouseUp,
        KeyDown,
        ScrollWheel,
        MouseDrag,
        MouseMove,
        Layout,
        Repaint,
        Used,
    }

    public enum KeyCode
    {
        None,
        Return,
    }

    [Flags]
    public enum EventModifiers
    {
        Shift = 1,
        Control = 2,
        Alt = 4,
        Command = 8,
    }

    public struct Vector2(float x, float y)
    {
        public float x = x,
            y = y;
    }

    public struct Rect(float x, float y, float width, float height)
    {
        public float x = x,
            y = y,
            width = width,
            height = height;

        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;
    }

    public struct Matrix4x4
    {
        public static Matrix4x4 identity => default;
    }

    public class Event
    {
        public static Event current = new();
        public EventType type,
            rawType;
        public Vector2 mousePosition,
            delta;
        public int button;
        public KeyCode keyCode;
        public char character;
        public EventModifiers modifiers;
        public bool alt,
            shift;
    }

    public static class GUI
    {
        public static Matrix4x4 matrix;

        public static Action? OnDispatchInput,
            OnProcessWindows;

        public static void DispatchInputToWindows() => OnDispatchInput?.Invoke();

        public static void ProcessPendingWindows() => OnProcessWindows?.Invoke();

        public static void Label(Rect rect, string text) { }

        public static bool Button(Rect rect, string text) => false;

        public static string TextField(Rect rect, string text) => text;
    }

    public static class GUIUtility
    {
        public static int keyboardControl,
            hotControl;

        public static void ResetGlobalState() { }

        public static void ResetControlIds() { }
    }
}

namespace UnityStubs.Gui
{
    public static class GuiBackend
    {
        public static object Current = null!;
    }
}

namespace BrowserAdapters
{
    public class BrowserGui
    {
        public static string? OutsideTargetTrace;

        public void BeginLabelRepaint() { }

        public long ProfileBakedTicks,
            ProfileNativeTicks;
        public int ProfileBakedRuns,
            ProfileBakedCharacters,
            ProfileNativeRuns,
            ProfileFallbackRuns;

        public void ResetTextProfile() { }

        public bool SuppressDrawing;
        public double LastResourceSweepMs,
            LastSerializationMs;
        public bool LastPacketReused;
        public Action? OnDrain;

        public static BrowserGui Install() => new();

        public void Discard() { }

        public string Drain()
        {
            OnDrain?.Invoke();
            return "[]";
        }
    }

    public static class BrowserClipboard
    {
        public static Action<string>? OnSupply;

        public static void Supply(string text, bool image) => OnSupply?.Invoke(text);

        public static void EndEvent() { }
    }
}
