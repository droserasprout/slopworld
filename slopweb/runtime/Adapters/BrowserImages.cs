// Decode encoded preview images without desktop libstb. Native texture ownership,
// bounded reader admission, and upload invalidation remain with their callers.
using StbImageSharp;
using UnityEngine;

namespace BrowserAdapters;

public static class BrowserImages
{
    public static bool Load(Texture2D texture, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false);
        ImageResult image;
        try
        {
            var info = ImageInfo.FromStream(stream);
            if (
                info == null
                || info.Value.Width <= 0
                || info.Value.Height <= 0
                || (long)info.Value.Width * info.Value.Height > 32 * 1024 * 1024
            )
                return false;
            stream.Position = 0;
            image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        }
        // StbImageSharp raises InvalidOperationException when pixel decoding fails.
        catch (InvalidOperationException)
        {
            return false;
        }
        if (!texture.Reinitialize(image.Width, image.Height, TextureFormat.RGBA32, false))
            return false;
        // stb rows are top-down; Unity's public pixel API is bottom-up.
        var pixels = new Color32[image.Width * image.Height];
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
        {
            int source = (y * image.Width + x) * 4;
            pixels[(image.Height - y - 1) * image.Width + x] = new Color32(
                image.Data[source],
                image.Data[source + 1],
                image.Data[source + 2],
                image.Data[source + 3]
            );
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false); // Canvas still needs readable CPU pixels.
        BrowserGui.TextureApplied(texture);
        return true;
    }
}
