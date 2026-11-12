using System.Numerics;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

public partial class Probe
{
    // Invoke the installed input owner, not a parallel browser model. Reflection
    // crosses its internal event-source boundary without altering game assemblies.
    private static object? InputCall(string name, params object[] args) =>
        Invoke("Waylith", "Waylith.Input.InputState", name, args);

    private static object InputKey(string name) =>
        Enum.Parse(Type("Waylith", "Waylith.Input.Key"), name);

    private static object? UnityInput(string name, params object[] args) =>
        Invoke("UnityStubs", "UnityEngine.InputSystem.InputSystem", name, args);

    private static object UnityVector(float x, float y) =>
        Activator.CreateInstance(Type("UnityStubs", "UnityEngine.Vector2"), x, y)!;

    [JSExport]
    public static void InputFrame(string batch, int height)
    {
        InputCall("BeginFrame");
        UnityInput("BeginFrame");
        using var json = JsonDocument.Parse(batch);
        foreach (var item in json.RootElement.EnumerateArray())
        {
            Vector2 Position() =>
                new(item.GetProperty("x").GetSingle(), item.GetProperty("y").GetSingle());
            object UnityPosition() => UnityVector(Position().X, height - Position().Y);
            switch (item.GetProperty("kind").GetString())
            {
                case "key":
                    InputCall(
                        "OnKey",
                        InputKey(item.GetProperty("key").GetString()!),
                        item.GetProperty("down").GetBoolean()
                    );
                    UnityInput(
                        "QueueKeyChange",
                        Enum.Parse(
                            Type("UnityStubs", "UnityEngine.InputSystem.Key"),
                            item.GetProperty("key").GetString()!
                        ),
                        item.GetProperty("down").GetBoolean()
                    );
                    break;
                case "repeat":
                    UnityInput(
                        "QueueKeyRepeat",
                        Enum.Parse(
                            Type("UnityStubs", "UnityEngine.InputSystem.Key"),
                            item.GetProperty("key").GetString()!
                        )
                    );
                    break;
                case "move":
                    InputCall("OnMouseMove", Position());
                    UnityInput("QueueMouseMove", UnityPosition());
                    break;
                case "button":
                    InputCall("OnMouseMove", Position());
                    InputCall(
                        "OnMouseButton",
                        Enum.ToObject(
                            Type("Waylith", "Waylith.Input.MouseButton"),
                            item.GetProperty("button").GetInt32()
                        ),
                        item.GetProperty("down").GetBoolean(),
                        Position()
                    );
                    UnityInput("QueueMouseMove", UnityPosition());
                    UnityInput(
                        "QueueMouseButton",
                        item.GetProperty("button").GetInt32(),
                        item.GetProperty("down").GetBoolean(),
                        UnityPosition()
                    );
                    break;
                case "scroll":
                    InputCall(
                        "OnScroll",
                        new Vector2(
                            item.GetProperty("dx").GetSingle(),
                            item.GetProperty("dy").GetSingle()
                        ),
                        Position()
                    );
                    UnityInput(
                        "QueueMouseScroll",
                        UnityVector(
                            item.GetProperty("dx").GetSingle(),
                            item.GetProperty("dy").GetSingle()
                        ),
                        UnityPosition()
                    );
                    break;
                case "reset":
                    UnityInput("ResetAllInput");
                    UnityInput("BeginFrame");
                    InputCall("ResetAll");
                    InputCall("BeginFrame");
                    break;
                default:
                    throw new ArgumentException("Unknown browser input event");
            }
        }
    }
}
