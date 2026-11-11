// Exercise actual adapter uploads and packets against installed CPU-only textures.
using System.Reflection;
using System.Text.Json;
using BrowserAdapters;
using UnityEngine;

static class ImageChecks
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run()
    {
        if (BrowserGui.ConfigureImageVersionCache(false))
            throw new Exception("Image version cache must default off");
        if (BrowserGui.ConfigureImageVersionCache(true))
            throw new Exception("Enabling image version cache returned wrong previous value");
        if (!BrowserGui.ConfigureImageVersionCache(false))
            throw new Exception("Disabling image version cache returned wrong previous value");
        var reference = new BrowserGui();
        var cached = new BrowserGui();
        using var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.SetPixels32([Color.red, Color.green, Color.blue, Color.white]);
        using var other = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        other.SetPixels32([Color.white, Color.blue, Color.green, Color.red]);

        void Draw(Texture2D image)
        {
            BrowserGui.ConfigureImageVersionCache(false);
            Paint(reference, image);
            BrowserGui.ConfigureImageVersionCache(true);
            Paint(cached, image);
        }
        string Drain(int uploads, string scenario)
        {
            string expected = reference.Drain();
            string actual = cached.Drain();
            if (actual != expected)
                throw new Exception("Image version cache changed packet: " + scenario);
            using var packet = JsonDocument.Parse(actual);
            int count = packet
                .RootElement.EnumerateArray()
                .Count(c => c.GetProperty("kind").GetString() == "image");
            if (count != uploads)
                throw new Exception($"Expected {uploads} uploads for {scenario}, got {count}");
            CheckReleased(reference);
            CheckReleased(cached);
            return actual;
        }

        Draw(texture);
        for (int i = 0; i < 100; i++)
            Draw(texture);
        // Mutation while the cache is populated must observe the same version object.
        texture.SetPixels32([Color.white, Color.red, Color.green, Color.blue]);
        texture.Apply(false, false);
        BrowserGui.TextureApplied(texture);
        Draw(texture);
        Draw(other);
        Draw(texture);
        Drain(3, "repeated draws, in-pass Apply, texture switching");
        Draw(texture);
        Drain(0, "stable texture in next pass");

        // Replace backing without Apply or a dimension change. Its identity remains
        // an independent invalidation input even when the version cache hits.
        Draw(texture);
        texture
            .CoreTexture.GetType()
            .GetProperty("CpuData")!
            .SetValue(texture.CoreTexture, other.CoreTexture.CpuData);
        Draw(texture);
        Drain(1, "changed CPU backing without Apply");
        Draw(texture);
        texture.CoreTexture.width = 1;
        texture.CoreTexture.height = 4;
        Draw(texture);
        Drain(1, "changed dimensions without Apply");

        BrowserGui.TextureApplied(texture);
        Draw(texture);
        reference.Discard();
        cached.Discard();
        CheckReleased(cached);
        Draw(texture);
        Drain(1, "discarded invalidation reuploads");
        using var fresh = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Draw(fresh);
        reference.Discard();
        cached.Discard();
        CheckReleased(cached);
        Draw(fresh);
        Drain(1, "discarded first upload reuploads");

        // Simulate a recycled engine ID with identical version/dimensions/backing.
        // Only managed texture identity distinguishes these upload owners.
        using var replacement = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        replacement
            .CoreTexture.GetType()
            .GetProperty("CpuData")!
            .SetValue(replacement.CoreTexture, other.CoreTexture.CpuData);
        typeof(UnityEngine.Object)
            .GetField("fallbackInstanceId", Fields)!
            .SetValue(replacement, other.GetInstanceID());
        Draw(other);
        Draw(replacement);
        Drain(1, "recycled ID with shared backing");
        Draw(replacement);
        UnityEngine.Object.DestroyImmediate(replacement);
        using var disposed = JsonDocument.Parse(Drain(0, "disposed texture cleanup"));
        if (
            disposed
                .RootElement.EnumerateArray()
                .Count(c => c.GetProperty("kind").GetString() == "releaseImage") != 1
        )
            throw new Exception("Disposed texture upload was not released");
        Drain(0, "repeated disposed texture cleanup");
        BrowserGui.ConfigureImageVersionCache(false);
        Console.WriteLine(
            "Image version cache off/on packets match for repeated draws, Apply, CPU backing, dimensions, switches, discard, ID reuse and disposal; pass completion releases cached texture references."
        );
    }

    internal static void RunCells(int fontIndex)
    {
        var reference = new BrowserGui();
        var cached = new BrowserGui();
        string Frame(BrowserGui gui, bool enabled)
        {
            BrowserGui.ConfigureImageVersionCache(enabled);
            for (int i = 0; i < 2671; i++)
                gui.DrawText(
                    new Rect(i % 100 * 8, i / 100 * 18, 8, 18),
                    ((char)('!' + i % 90)).ToString(),
                    fontIndex,
                    15,
                    FontStyle.Normal,
                    TextAnchor.UpperLeft,
                    false,
                    Color.white,
                    default,
                    false
                );
            return gui.Drain();
        }
        for (int repeat = 0; repeat < 3; repeat++)
            if (Frame(reference, false) != Frame(cached, true))
                throw new Exception("Image version cache changed terminal cell packet");
        BrowserGui.ConfigureImageVersionCache(false);
        Console.WriteLine(
            "Image version cache preserves exact single-cell glyph packets across first upload and repeated frames."
        );
    }

    static void Paint(BrowserGui gui, Texture2D texture) =>
        gui.DrawTextureWithTexCoords(
            new Rect(1.25f, 2.5f, 20, 30),
            texture,
            new Rect(.1f, .2f, .7f, .8f),
            new Color(.3f, .6f, .9f, .5f)
        );

    static void CheckReleased(BrowserGui gui)
    {
        foreach (string name in new[] { "lastImageTexture", "lastImageVersion" })
            if (typeof(BrowserGui).GetField(name, Fields)!.GetValue(gui) != null)
                throw new Exception("Completed pass retained " + name);
    }
}
