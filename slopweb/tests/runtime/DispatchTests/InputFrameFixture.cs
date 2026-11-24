using System.Numerics;
using BrowserAdapters;
using Waylith.Input;
using NativeInput = UnityEngine.InputSystem.InputSystem;

// Exercise the real input-frame adapter with the installed engine's event signatures.
static class InputFrameFixture
{
    internal static readonly List<string> Calls = [];

    public static void Check()
    {
        var runtime = new WorkspaceRuntime();
        runtime.InputFrame("[]", 240);
        runtime.InputFrame(
            """
            [{"kind":"key","key":"F12","down":true},
             {"kind":"repeat","key":"F12"},
             {"kind":"button","button":0,"down":true,"x":12,"y":34},
             {"kind":"scroll","dx":0,"dy":1,"x":12,"y":34}]
            """,
            240
        );
        if (!InputState.IsKeyDown(Key.F12) || !InputState.IsMouseButtonDown(MouseButton.Left))
            throw new Exception("Physical presses were not delivered");
        if (NativeInput.LastPosition != new UnityEngine.Vector2(12, 206))
            throw new Exception("Incorrect Unity mouse coordinates");
        runtime.InputFrame("""[{"kind":"reset"}]""", 240);
        if (InputState.IsKeyDown(Key.F12) || InputState.IsMouseButtonDown(MouseButton.Left))
            throw new Exception("Focus loss must release physical input");
        int releases = Calls.Count(call => call == "waylith.OnKey");
        runtime.InputFrame("""[{"kind":"reset"}]""", 240);
        if (Calls.Count(call => call == "waylith.OnKey") != releases)
            throw new Exception("Repeated reset must not release already released keys");
        runtime.InputFrame("""[{"kind":"key","key":"F12","down":true}]""", 240);
        runtime.InputFrame("""[{"kind":"key","key":"F12","down":false}]""", 240);
        if (InputState.IsKeyDown(Key.F12) || !Calls.Contains("unity.QueueMouseScroll"))
            throw new Exception("Input must continue after reset and reach both owners");
        if (!Calls.Take(2).SequenceEqual(["waylith.BeginFrame", "unity.BeginFrame"]))
            throw new Exception("Frame setup order changed");
    }
}

namespace Waylith.Input
{
    public enum Key
    {
        F12,
    }

    public enum MouseButton
    {
        Left,
    }

    public static class InputState
    {
        static readonly HashSet<Key> Keys = [];
        static readonly HashSet<MouseButton> Buttons = [];
        public static Vector2 MousePosition { get; private set; }

        public static bool IsKeyDown(Key key) => Keys.Contains(key);

        public static bool IsMouseButtonDown(MouseButton button) => Buttons.Contains(button);

        internal static void BeginFrame(bool keep)
        {
            if (keep)
                throw new Exception("Frame retained stale events");
            InputFrameFixture.Calls.Add("waylith.BeginFrame");
        }

        internal static void OnKey(Key key, bool down, DayOfWeek? command)
        {
            if (command.HasValue)
                throw new Exception("Browser supplied a native edit command");
            InputFrameFixture.Calls.Add("waylith.OnKey");
            if (down)
                Keys.Add(key);
            else
                Keys.Remove(key);
        }

        internal static void OnMouseMove(Vector2 position) => MousePosition = position;

        internal static void OnMouseButton(MouseButton button, bool down, Vector2 position)
        {
            if (position != MousePosition)
                throw new Exception("Button preceded mouse motion");
            if (down)
                Buttons.Add(button);
            else
                Buttons.Remove(button);
        }

        internal static void OnScroll(Vector2 delta, Vector2 position, Vector2? touchpad)
        {
            if (touchpad.HasValue || position != MousePosition || delta != Vector2.UnitY)
                throw new Exception("Scroll payload changed");
        }
    }
}

namespace UnityEngine
{
    public readonly record struct Vector2(float X, float Y);
}

namespace UnityEngine.InputSystem
{
    public enum Key
    {
        F12,
    }

    public static class InputSystem
    {
        public static Vector2 LastPosition { get; private set; }

        public static void BeginFrame() => InputFrameFixture.Calls.Add("unity.BeginFrame");

        public static void QueueKeyChange(Key key, bool down) { }

        public static void QueueKeyRepeat(Key key) { }

        public static void QueueMouseMove(Vector2 position) => LastPosition = position;

        public static void QueueMouseButton(int button, bool down, Vector2 position) =>
            LastPosition = position;

        public static void QueueMouseScroll(Vector2 delta, Vector2 position)
        {
            LastPosition = position;
            InputFrameFixture.Calls.Add("unity.QueueMouseScroll");
        }

        public static void ResetAllInput() { }
    }
}
