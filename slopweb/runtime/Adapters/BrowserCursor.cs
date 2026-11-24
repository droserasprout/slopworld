// Native GameCursor owns appearance and animation. Only readable texture pixels
// and Unity's final hotspot cross this presentation boundary.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using SlopWorld;
using UnityEngine;

namespace BrowserAdapters;

public static class BrowserCursor
{
    sealed record Frame(int Id, int Width, int Height, string Pixels);

    static readonly ConditionalWeakTable<Texture2D, Frame> frames = new();
    static bool installed;
    static int nextId;
    static string pending = "";

    public static void Install()
    {
        WorkspaceDrawing.RequireEntry();
        GameCursor.Apply();
        installed = true;
    }

    // Run after BeginFrame advances Unity time, before presenting the frame.
    // Browser GUI batches retain every button press, including a down/up pair
    // between frames that polling Input.GetMouseButtonDown would miss.
    public static void Update(JsonElement batch)
    {
        if (!installed)
            throw new InvalidOperationException("Install browser cursor before updating");
        foreach (var ev in batch.EnumerateArray())
            if (ev.GetProperty("kind").GetString() == "down")
                GameCursor.Click();
        GameCursor.Tick();
    }

    public static Color[] ReadBack(Texture2D source) => source.GetPixels();

    // The mod has already rotated and colored the frame for its final hotspot.
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
