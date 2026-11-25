// Dispatch browser events into installed Unity controls; this checkpoint does not
// replace the engine's eventual OnGUI frame traversal or the SlopWorld workspace.
using System.Diagnostics;
using System.Text.Json;
using UnityEngine;

namespace BrowserAdapters;

public static class BrowserGuiInput
{
    public static bool Profile;
    static bool skipDiscardedDrawing = true;

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

    static Vector2 pointer;

    public static string Dispatch(
        string batch,
        BrowserGui target,
        Action draw,
        Action? afterInput = null
    )
    {
        using var json = JsonDocument.Parse(batch);
        return DispatchBatch(json.RootElement, target, draw, afterInput, -1, false);
    }

    public static string DispatchBatch(
        JsonElement batch,
        BrowserGui target,
        Action draw,
        Action? afterInput,
        double retainedPacket,
        bool commandsOnly
    )
    {
        long started = Profile ? Stopwatch.GetTimestamp() : 0;
        long allocated = Profile ? GC.GetAllocatedBytesForCurrentThread() : 0;
        int collections = Profile ? GC.CollectionCount(0) : 0;
        foreach (var item in batch.EnumerateArray())
        {
            var kind = item.GetProperty("kind").GetString();
            if (kind == "restoreTargets")
            {
                target.InvalidateRenderTargets();
                continue;
            }
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
        if (Profile)
            target.ResetTextProfile();
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
        if (commandsOnly)
            return commands;
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
                        bakedTextMs = target.ProfileBakedTicks * 1000d / Stopwatch.Frequency,
                        bakedPrepareMs = Ms(0, target.ProfileBakedPrepareTicks),
                        bakedGlyphDrawMs = Ms(0, target.ProfileBakedDrawTicks),
                        clippedTailTraceMs = Ms(0, target.ProfileClippedTailTraceTicks),
                        nativeTextMs = target.ProfileNativeTicks * 1000d / Stopwatch.Frequency,
                        bakedTextRuns = target.ProfileBakedRuns,
                        bakedTextCharacters = target.ProfileBakedCharacters,
                        nativeTextRuns = target.ProfileNativeRuns,
                        fallbackTextRuns = target.ProfileFallbackRuns,
                        drainMs = Ms(repaintEnd, serializationEnd),
                        resourceSweepMs = target.LastResourceSweepMs,
                        serializationMs = target.LastSerializationMs,
                        packetReused = target.LastPacketReused ? 1 : 0,
                        packetMatchedCommands = target.LastMatchedCommands,
                        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated,
                        gen0Collections = GC.CollectionCount(0) - collections,
                        commandChars = commands.Length,
                    }
                );
        }
        // Commands are already JSON. Avoid parsing and serializing the entire frame
        // a second time solely to add the small control-check envelope.
        var id = target.PacketId;
        if (retainedPacket >= 0 && id != 0)
        {
            string number = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (retainedPacket == id)
                return "{\"repeat\":" + number + timing + "}";
            return "{\"packet\":" + number + ",\"commands\":" + commands + timing + "}";
        }
        return "{\"commands\":" + commands + timing + "}";
    }
}
