using UnityEngine;
using Verse;

namespace BrowserAdapters;

// Session-only rows have no pawn portrait. Reuse authored faceplates with a
// stable identity color; neither appearance nor rendering needs a colony pawn.
public static class BrowserAvatars
{
    static readonly string[] colors = { "Blue", "Green", "Purple", "Red", "White", "Yellow" };
    static readonly Dictionary<Texture2D, Rect> crops = new();

    static Rect Crop(Texture2D texture)
    {
        if (crops.TryGetValue(texture, out var crop))
            return crop;
        // Faceplates include transparent pawn-head padding. Crop only that
        // padding so a compact session row still gets a readable avatar.
        int left = texture.width,
            bottom = texture.height,
            right = 0,
            top = 0;
        var pixels = texture.GetPixels32();
        for (int y = 0; y < texture.height; y++)
        for (int x = 0; x < texture.width; x++)
            if (pixels[y * texture.width + x].a > 8)
            {
                left = Math.Min(left, x);
                right = Math.Max(right, x + 1);
                bottom = Math.Min(bottom, y);
                top = Math.Max(top, y + 1);
            }
        crop =
            right > left && top > bottom
                ? new Rect(
                    (float)left / texture.width,
                    (float)bottom / texture.height,
                    (float)(right - left) / texture.width,
                    (float)(top - bottom) / texture.height
                )
                : new Rect(0, 0, 1, 1);
        crops.Add(texture, crop);
        return crop;
    }

    public static Rect Draw(Rect rect, object? info)
    {
        if (info == null)
            return rect;
        var type = info.GetType();
        if (
            (bool)type.GetField("Host")!.GetValue(info)!
            || string.IsNullOrEmpty((string?)type.GetField("Agent")!.GetValue(info))
        )
            return rect;
        string name = (string)type.GetField("Name")!.GetValue(info)!;
        uint hash = 2166136261;
        foreach (char c in name)
            hash = unchecked((hash ^ c) * 16777619);
        float size = Math.Min(rect.height, 28f);
        if (Event.current.type == EventType.Repaint)
        {
            var texture = ContentFinder<Texture2D>.Get(
                "SlopWorld/RobotFace_" + colors[hash % colors.Length] + "_south"
            );
            var previous = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(
                new Rect(rect.x, rect.y + (rect.height - size) / 2, size, size),
                texture,
                Crop(texture)
            );
            GUI.color = previous;
        }
        rect.x += size + 4;
        rect.width = Math.Max(0, rect.width - size - 4);
        return rect;
    }
}
