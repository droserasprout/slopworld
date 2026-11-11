// The browser owns only exported appearance pixels, never native pawns or save data.
using System.Buffers.Binary;
using UnityEngine;

namespace BrowserAdapters;

public static class BrowserPortraits
{
    static readonly Dictionary<string, Texture2D> textures = new(StringComparer.Ordinal);
    static long bytes;

    public static void Install(string session, byte[] data)
    {
        WorkspaceDrawing.RequireEntry();
        if (
            string.IsNullOrEmpty(session)
            || session.Length > 256
            || textures.ContainsKey(session)
            || textures.Count >= 1024
            || data.Length < 8
        )
            throw new InvalidDataException("Invalid portrait identity or inventory");
        int width = BinaryPrimitives.ReadInt32LittleEndian(data);
        int height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        if (
            width <= 0
            || height <= 0
            || width > 2048
            || height > 2048
            || data.Length != 8L + width * (long)height * 4
            || bytes + data.Length > 64 * 1024 * 1024
        )
            throw new InvalidDataException("Invalid portrait pixel dimensions or size");
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = 8 + (y * width + x) * 4;
            pixels[(height - y - 1) * width + x] = new Color32(
                data[offset],
                data[offset + 1],
                data[offset + 2],
                data[offset + 3]
            );
        }
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        textures.Add(session, texture);
        bytes += data.Length;
    }

    public static void Bind()
    {
        var mod = AppDomain
            .CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "SlopWorld");
        mod.GetType("SlopWorld.SessionPortraits", true)!
            .GetField("Source")!
            .SetValue(
                null,
                new Func<string, Texture?>(session => textures.GetValueOrDefault(session))
            );
    }
}
