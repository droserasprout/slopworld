// Dispatch browser events into installed Unity controls; this checkpoint does not
// replace the engine's eventual OnGUI frame traversal or the SlopWorld workspace.
using System.Diagnostics;
using System.Text.Json;
using UnityEngine;

namespace BrowserAdapters;

public static class BrowserGuiInput
{
    public static bool Profile;
    static bool skipDiscardedDrawing;

    public static bool ConfigureDiscardedDrawing(bool skip)
    {
        bool previous = skipDiscardedDrawing;
        skipDiscardedDrawing = skip;
        return previous;
    }

    static void DrawUnpresented(BrowserGui target, Action draw)
    {
        bool previous = target.SuppressDrawing;
        target.SuppressDrawing = skipDiscardedDrawing;
        try
        {
            // Native input/layout, coordinates and hit tests still execute.
            // Only raster-command preparation is omitted for discarded passes.
            draw();
        }
        finally
        {
            target.SuppressDrawing = previous;
        }
    }

    public static string DiscardedDrawingProbe()
    {
        var previousBackend = UnityStubs.Gui.GuiBackend.Current;
        bool previousSkip = skipDiscardedDrawing,
            previousProfile = Profile;
        var previousEvent = Event.current;
        var previousMatrix = GUI.matrix;
        var previousPointer = pointer;
        try
        {
            Profile = false;
            string Paint(bool skip)
            {
                skipDiscardedDrawing = skip;
                var gui = BrowserGui.Install();
                int input = 0,
                    layout = 0,
                    repaint = 0;
                var result = Dispatch(
                    "[{\"kind\":\"move\",\"x\":-50,\"y\":-50}]",
                    gui,
                    () =>
                    {
                        if (Event.current.type == EventType.MouseMove)
                            input++;
                        if (Event.current.type == EventType.Layout)
                            layout++;
                        if (Event.current.type == EventType.Repaint)
                            repaint++;
                        GUI.Label(new Rect(20, 20, 250, 50), "Native discarded-pass check");
                    }
                );
                if (input != 1 || layout != 1 || repaint != 1)
                    throw new InvalidOperationException(
                        "Suppression skipped native event dispatch"
                    );
                using var json = JsonDocument.Parse(result);
                return json.RootElement.GetProperty("commands").GetRawText();
            }
            string baseline = Paint(false),
                suppressed = Paint(true);
            if (baseline != suppressed || baseline == "[]")
                throw new InvalidOperationException(
                    "Discarded-pass suppression changed repaint commands"
                );
            var target = BrowserGui.Install();
            try
            {
                DrawUnpresented(target, () => throw new InvalidDataException("probe"));
            }
            catch (InvalidDataException) { }
            if (target.SuppressDrawing)
                throw new InvalidOperationException("Suppression leaked after a failed input pass");
            return "ok";
        }
        finally
        {
            skipDiscardedDrawing = previousSkip;
            Profile = previousProfile;
            UnityStubs.Gui.GuiBackend.Current = previousBackend;
            Event.current = previousEvent;
            GUI.matrix = previousMatrix;
            pointer = previousPointer;
        }
    }

    static BrowserGui? backend;
    static int clicks;
    static string text = "";
    static Vector2 pointer;

    public static string Frame(string batch)
    {
        backend ??= BrowserGui.Install();
        return Dispatch(batch, backend, Controls);
    }

    public static string Dispatch(
        string batch,
        BrowserGui target,
        Action draw,
        Action? afterInput = null
    )
    {
        long started = Profile ? Stopwatch.GetTimestamp() : 0;
        long allocated = Profile ? GC.GetAllocatedBytesForCurrentThread() : 0;
        int collections = Profile ? GC.CollectionCount(0) : 0;
        using var json = JsonDocument.Parse(batch);
        foreach (var item in json.RootElement.EnumerateArray())
        {
            var kind = item.GetProperty("kind").GetString();
            if (kind == "clipboard")
            {
                BrowserClipboard.Supply(
                    item.GetProperty("text").GetString() ?? "",
                    item.TryGetProperty("image", out var image) && image.GetBoolean()
                );
                continue;
            }
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
            // Installed Waylith stores these two properties independently from
            // modifiers; native navigation reads alt/shift directly.
            ev.alt = (ev.modifiers & EventModifiers.Alt) != 0;
            ev.shift = (ev.modifiers & EventModifiers.Shift) != 0;
            GUI.matrix = Matrix4x4.identity;
            Event.current = ev;
            GUIUtility.ResetControlIds();
            DrawUnpresented(target, draw);
            BrowserClipboard.EndEvent();
            target.Discard(); // Input passes change state; only repaint commands are presented.
        }
        // Match the native keyboard-dispatch boundary: keep all UTF-16 units and
        // named keys ordered, then send pending input before layout/repaint work.
        afterInput?.Invoke();
        long inputEnd = Profile ? Stopwatch.GetTimestamp() : 0;
        GUI.matrix = Matrix4x4.identity;
        Event.current = new Event
        {
            type = EventType.Layout,
            rawType = EventType.Layout,
            mousePosition = pointer,
        };
        GUIUtility.ResetControlIds();
        DrawUnpresented(target, draw);
        target.Discard();
        long layoutEnd = Profile ? Stopwatch.GetTimestamp() : 0;
        GUI.matrix = Matrix4x4.identity;
        Event.current = new Event
        {
            type = EventType.Repaint,
            rawType = EventType.Repaint,
            mousePosition = pointer,
        };
        GUIUtility.ResetControlIds();
        draw();
        long repaintEnd = Profile ? Stopwatch.GetTimestamp() : 0;
        string commands = target.Drain();
        string timing = "";
        if (Profile)
        {
            long serializationEnd = Stopwatch.GetTimestamp();
            double Ms(long from, long to) => (to - from) * 1000d / Stopwatch.Frequency;
            timing =
                ",\"profile\":"
                + JsonSerializer.Serialize(
                    new
                    {
                        inputMs = Ms(started, inputEnd),
                        layoutMs = Ms(inputEnd, layoutEnd),
                        repaintMs = Ms(layoutEnd, repaintEnd),
                        drainMs = Ms(repaintEnd, serializationEnd),
                        resourceSweepMs = target.LastResourceSweepMs,
                        serializationMs = target.LastSerializationMs,
                        packetReused = target.LastPacketReused ? 1 : 0,
                        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated,
                        gen0Collections = GC.CollectionCount(0) - collections,
                        commandChars = commands.Length,
                    }
                );
        }
        // Commands are already JSON. Avoid parsing and serializing the entire frame
        // a second time solely to add the small control-check envelope.
        return "{\"clicks\":"
            + clicks
            + ",\"text\":"
            + JsonSerializer.Serialize(text)
            + ",\"commands\":"
            + commands
            + timing
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
