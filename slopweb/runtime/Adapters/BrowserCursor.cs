// Native GameCursor owns appearance and animation. Only readable texture pixels
// and Unity's final hotspot cross this presentation boundary.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using UnityEngine;

namespace BrowserAdapters;

public static class BrowserCursor
{
    sealed record Frame(int Id, int Width, int Height, string Pixels);

    static readonly ConditionalWeakTable<Texture2D, Frame> frames = new();
    static Action? tick;
    static Action? click;
    static int nextId;
    static string pending = "";

    public static void Install()
    {
        WorkspaceDrawing.RequireEntry();
        var mod = AppDomain
            .CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "SlopWorld");
        var cursor = mod.GetType("SlopWorld.GameCursor")!;
        tick = cursor.GetMethod("Tick")!.CreateDelegate<Action>();
        click = cursor.GetMethod("Click")!.CreateDelegate<Action>();
        cursor.GetMethod("Apply")!.Invoke(null, null);
    }

    // Run after BeginFrame advances Unity time, before presenting the frame.
    // Browser GUI batches retain every button press, including a down/up pair
    // between frames that polling Input.GetMouseButtonDown would miss.
    public static void Update(string batch)
    {
        if (tick == null)
            throw new InvalidOperationException("Install browser cursor before updating");
        using var events = JsonDocument.Parse(batch);
        foreach (var ev in events.RootElement.EnumerateArray())
            if (ev.GetProperty("kind").GetString() == "down")
                click!();
        tick();
    }

    public static Color[] ReadBack(Texture2D source) => source.GetPixels();

    // Offline staging redirects the native CursorTextures.Show SetCursor calls
    // here. Do not rebuild, recolor, mirror or rotate the native frame in JS.
    public static void SetCursor(Texture2D texture, Vector2 hotspot, CursorMode mode)
    {
        if (texture == null)
        {
            pending = "null";
            return;
        }
        var frame = frames.GetValue(texture, Capture);
        pending = JsonSerializer.Serialize(
            new
            {
                id = frame.Id,
                width = frame.Width,
                height = frame.Height,
                pixels = frame.Pixels,
                x = hotspot.x,
                y = hotspot.y,
            }
        );
    }

    static Frame Capture(Texture2D texture)
    {
        var pixels = texture.GetPixels32();
        var source = MemoryMarshal.AsBytes(pixels.AsSpan());
        var bytes = new byte[source.Length];
        int stride = texture.width * 4;
        // Unity texture rows are bottom-up; browser ImageData is top-down.
        for (int row = 0; row < texture.height; row++)
            source
                .Slice((texture.height - row - 1) * stride, stride)
                .CopyTo(bytes.AsSpan(row * stride));
        return new Frame(++nextId, texture.width, texture.height, Convert.ToBase64String(bytes));
    }

    public static string Take()
    {
        var result = pending;
        pending = "";
        return result;
    }
}
