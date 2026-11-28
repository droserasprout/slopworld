using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Waylith.Input;
using NativeInput = UnityEngine.InputSystem.InputSystem;
using NativeKey = UnityEngine.InputSystem.Key;
using NativeVector = UnityEngine.Vector2;

namespace BrowserAdapters;

public sealed partial class WorkspaceRuntime
{
    // Installed Waylith adds edit-command metadata and the frame event-retention flag
    // after the pinned reference build. Bind its event producer on the first input frame.
    static class InputEvents
    {
        const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        internal static readonly Action<bool> Begin = Bind<Action<bool>>(
            "BeginFrame",
            typeof(bool)
        );
        internal static readonly Action<Vector2> Move = Bind<Action<Vector2>>(
            "OnMouseMove",
            typeof(Vector2)
        );
        internal static readonly Action<MouseButton, bool, Vector2> Button = Bind<
            Action<MouseButton, bool, Vector2>
        >("OnMouseButton", typeof(MouseButton), typeof(bool), typeof(Vector2));
        internal static readonly Action<Vector2, Vector2, Vector2?> Scroll = Bind<
            Action<Vector2, Vector2, Vector2?>
        >("OnScroll", typeof(Vector2), typeof(Vector2), typeof(Vector2?));

        // The optional edit command is private to the installed game. Browser keys
        // carry no edit command, so its type never needs to enter the contract.
        internal static readonly MethodInvoker Key = MethodInvoker.Create(
            typeof(InputState)
                .GetMethods(Flags)
                .Single(m =>
                    m.Name == "OnKey"
                    && m.ReturnType == typeof(void)
                    && m.GetParameters() is [var key, var down, var command]
                    && key.ParameterType == typeof(Key)
                    && down.ParameterType == typeof(bool)
                    && Nullable.GetUnderlyingType(command.ParameterType) != null
                )
        );

        static T Bind<T>(string name, params Type[] parameters)
            where T : Delegate =>
            (
                typeof(InputState).GetMethod(name, Flags, parameters)
                ?? throw new MissingMethodException(typeof(InputState).FullName, name)
            ).CreateDelegate<T>();
    }

    static void BeginInputFrame()
    {
        InputEvents.Begin(false);
        NativeInput.BeginFrame();
    }

    static void ReleasePhysicalInput()
    {
        foreach (var key in Enum.GetValues<Key>())
            if (InputState.IsKeyDown(key))
                InputEvents.Key.Invoke(null, key, false, null);
        Vector2 position = InputState.MousePosition;
        foreach (var button in Enum.GetValues<MouseButton>())
            if (InputState.IsMouseButtonDown(button))
                InputEvents.Button(button, false, position);
    }

    public void InputFrame(string batch, int height)
    {
        BeginInputFrame();
        using var json = JsonDocument.Parse(batch);
        foreach (var item in json.RootElement.EnumerateArray())
        {
            Vector2 Position() =>
                new(item.GetProperty("x").GetSingle(), item.GetProperty("y").GetSingle());
            NativeVector UnityPosition(Vector2 p) => new(p.X, height - p.Y);
            switch (item.GetProperty("kind").GetString())
            {
                case "key":
                    var name = item.GetProperty("key").GetString()!;
                    bool down = item.GetProperty("down").GetBoolean();
                    InputEvents.Key.Invoke(null, Enum.Parse<Key>(name), down, null);
                    NativeInput.QueueKeyChange(Enum.Parse<NativeKey>(name), down);
                    break;
                case "repeat":
                    NativeInput.QueueKeyRepeat(
                        Enum.Parse<NativeKey>(item.GetProperty("key").GetString()!)
                    );
                    break;
                case "move":
                    var mouse = Position();
                    InputEvents.Move(mouse);
                    NativeInput.QueueMouseMove(UnityPosition(mouse));
                    break;
                case "button":
                    var position = Position();
                    int button = item.GetProperty("button").GetInt32();
                    bool pressed = item.GetProperty("down").GetBoolean();
                    InputEvents.Move(position);
                    InputEvents.Button((MouseButton)button, pressed, position);
                    NativeInput.QueueMouseMove(UnityPosition(position));
                    NativeInput.QueueMouseButton(button, pressed, UnityPosition(position));
                    break;
                case "scroll":
                    var delta = new Vector2(
                        item.GetProperty("dx").GetSingle(),
                        item.GetProperty("dy").GetSingle()
                    );
                    var at = Position();
                    InputEvents.Scroll(delta, at, null);
                    NativeInput.QueueMouseScroll(
                        new NativeVector(delta.X, delta.Y),
                        UnityPosition(at)
                    );
                    break;
                case "reset":
                    NativeInput.ResetAllInput();
                    NativeInput.BeginFrame();
                    ReleasePhysicalInput();
                    InputEvents.Begin(false);
                    break;
                default:
                    throw new ArgumentException("Unknown browser input event");
            }
        }
    }
}
