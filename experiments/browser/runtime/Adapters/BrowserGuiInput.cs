// Dispatch browser events into installed Unity controls; this checkpoint does not
// replace the engine's eventual OnGUI frame traversal or the SlopWorld workspace.
using System.Text.Json;
using UnityEngine;

namespace BrowserAdapters;

public static class BrowserGuiInput
{
    static BrowserGui? backend;
    static int clicks;
    static string text = "";
    static Vector2 pointer;

    public static string Frame(string batch)
    {
        backend ??= BrowserGui.Install();
        return Dispatch(batch, backend, Controls);
    }

    public static string Dispatch(string batch, BrowserGui target, Action draw)
    {
        using var json = JsonDocument.Parse(batch);
        foreach (var item in json.RootElement.EnumerateArray())
        {
            var kind = item.GetProperty("kind").GetString();
            if (kind == "reset")
            {
                // Installed ResetGlobalState resets drawing state, not pointer capture.
                // Browser focus loss has no native GLFW callback to clear these owners.
                foreach (var field in new[] { "_buttonDownActive", "_repeatDownActive" })
                    typeof(GUI)
                        .GetField(
                            field,
                            System.Reflection.BindingFlags.Static
                                | System.Reflection.BindingFlags.NonPublic
                        )!
                        .SetValue(null, false);
                GUIUtility.ResetGlobalState();
                GUIUtility.keyboardControl = 0;
                GUIUtility.hotControl = 0;
                continue;
            }
            if (item.TryGetProperty("x", out var x))
                pointer = new Vector2(x.GetSingle(), item.GetProperty("y").GetSingle());
            var type = kind switch
            {
                "down" => EventType.MouseDown,
                "up" => EventType.MouseUp,
                "key" => EventType.KeyDown,
                "text" => EventType.KeyDown,
                "wheel" => EventType.ScrollWheel,
                "drag" => EventType.MouseDrag,
                _ => EventType.MouseMove,
            };
            var ev = new Event
            {
                type = type,
                rawType = type,
                mousePosition = pointer,
            };
            if (item.TryGetProperty("button", out var button))
                ev.button = button.GetInt32();
            if (item.TryGetProperty("key", out var key))
                ev.keyCode = Enum.Parse<KeyCode>(key.GetString()!);
            if (kind == "text")
                ev.character = (char)item.GetProperty("character").GetInt32();
            if (kind == "wheel")
                ev.delta = new Vector2(
                    item.GetProperty("dx").GetSingle(),
                    item.GetProperty("dy").GetSingle()
                );
            foreach (
                var (name, flag) in new[]
                {
                    ("shift", EventModifiers.Shift),
                    ("control", EventModifiers.Control),
                    ("alt", EventModifiers.Alt),
                    ("command", EventModifiers.Command),
                }
            )
                if (
                    item.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.True
                )
                    ev.modifiers |= flag;
            GUI.matrix = Matrix4x4.identity;
            Event.current = ev;
            GUIUtility.ResetControlIds();
            draw();
            target.Discard(); // Input passes change state; only repaint commands are presented.
        }
        GUI.matrix = Matrix4x4.identity;
        Event.current = new Event
        {
            type = EventType.Layout,
            rawType = EventType.Layout,
            mousePosition = pointer,
        };
        GUIUtility.ResetControlIds();
        draw();
        target.Discard();
        GUI.matrix = Matrix4x4.identity;
        Event.current = new Event
        {
            type = EventType.Repaint,
            rawType = EventType.Repaint,
            mousePosition = pointer,
        };
        GUIUtility.ResetControlIds();
        draw();
        // Commands are already JSON. Avoid parsing and serializing the entire frame
        // a second time solely to add the small control-check envelope.
        return "{\"clicks\":"
            + clicks
            + ",\"text\":"
            + JsonSerializer.Serialize(text)
            + ",\"commands\":"
            + target.Drain()
            + "}";
    }

    static void Controls()
    {
        GUIUtility.ResetControlIds();
        GUI.Label(new Rect(20, 10, 450, 30), "Installed Unity controls — browser input");
        if (GUI.Button(new Rect(20, 50, 200, 40), $"Clicks: {clicks}"))
            clicks++;
        text = GUI.TextField(new Rect(20, 110, 450, 40), text);
    }
}
