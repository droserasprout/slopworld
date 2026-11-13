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
        CheckScaleModes();
        var reference = new BrowserGui();
        using var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.SetPixels32([Color.red, Color.green, Color.blue, Color.white]);
        using var other = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        other.SetPixels32([Color.white, Color.blue, Color.green, Color.red]);

        void Draw(Texture2D image)
        {
            Paint(reference, image);
        }
        string Drain(int uploads, string scenario)
        {
            string actual = reference.Drain();
            using var packet = JsonDocument.Parse(actual);
            int count = packet
                .RootElement.EnumerateArray()
                .Count(c => c.GetProperty("kind").GetString() == "image");
            if (count != uploads)
                throw new Exception($"Expected {uploads} uploads for {scenario}, got {count}");
            return actual;
        }

        Draw(texture);
        for (int i = 0; i < 100; i++)
            Draw(texture);
        // Mutation between draws must invalidate the uploaded pixels.
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
        // an independent invalidation input.
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
        Draw(texture);
        Drain(1, "discarded invalidation reuploads");
        using var fresh = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Draw(fresh);
        reference.Discard();
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
        UnityEngine.Object.DestroyImmediate(other);
        Draw(replacement);
        using var live = JsonDocument.Parse(Drain(0, "old owner disposal preserves replacement"));
        if (
            live
                .RootElement.EnumerateArray()
                .Any(c => c.GetProperty("kind").GetString() == "releaseImage")
        )
            throw new Exception("Old texture disposal released its live replacement");
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
        Console.WriteLine(
            "Image uploads track Apply, CPU backing, dimensions, switches, discard, ID reuse and disposal."
        );
    }

    static void CheckScaleModes()
    {
        var gui = new BrowserGui();
        using var wide = new Texture2D(8, 4, TextureFormat.RGBA32, false);
        using var tall = new Texture2D(4, 8, TextureFormat.RGBA32, false);
        var rect = new Rect(10, 20, 40, 40);
        var full = new Rect(0, 0, 1, 1);
        foreach (var image in new[] { wide, tall })
        {
            Check(image, rect, ScaleMode.StretchToFill, rect, full);
            Check(
                image,
                rect,
                ScaleMode.ScaleToFit,
                image == wide ? new Rect(10, 30, 40, 20) : new Rect(20, 20, 20, 40),
                full
            );
            Check(
                image,
                rect,
                ScaleMode.ScaleAndCrop,
                rect,
                image == wide ? new Rect(.25f, 0, .5f, 1) : new Rect(0, .25f, 1, .5f)
            );
            foreach (var mode in Enum.GetValues<ScaleMode>())
            {
                var matching = new Rect(1.25f, 2.5f, image.width * 3, image.height * 3);
                Check(image, matching, mode, matching, full);
                // Native fitting leaves nonpositive destination dimensions unchanged.
                foreach (var empty in new[] { new Rect(10, 20, 0, 40), new Rect(10, 20, 40, -1) })
                    Check(image, empty, mode, empty, full);
            }
        }

        // Fitting happens before clip rejection: a visible letterbox is not an image.
        gui.PushClipRect(new Rect(10, 20, 40, 5));
        gui.DrawTexture(rect, wide, ScaleMode.ScaleToFit, Color.white);
        using (var clipped = JsonDocument.Parse(gui.Drain()))
            if (
                clipped
                    .RootElement.EnumerateArray()
                    .Any(c => c.GetProperty("kind").GetString() == "imageBatch")
            )
                throw new Exception("ScaleToFit emitted an image outside the fitted drawing clip");
        gui.PopClipRect();
        gui.Discard();

        // The RenderTexture command path must receive the same fitted geometry.
        using var target = new RenderTexture(8, 4, 0);
        foreach (var mode in Enum.GetValues<ScaleMode>())
            Check(
                target,
                rect,
                mode,
                mode == ScaleMode.ScaleToFit ? new Rect(10, 30, 40, 20) : rect,
                mode == ScaleMode.ScaleAndCrop ? new Rect(.25f, 0, .5f, 1) : full
            );
        Console.WriteLine(
            "Texture scale modes preserve fitted destinations and cropped UVs through real adapter packets."
        );

        void Check(Texture image, Rect input, ScaleMode mode, Rect destination, Rect uv)
        {
            var tint = new Color(.3f, .6f, .9f, .5f);
            gui.DrawTexture(input, image, mode, tint);
            using var packet = JsonDocument.Parse(gui.Drain());
            string kind = image is RenderTexture ? "targetTexture" : "imageBatch";
            var value = packet
                .RootElement.EnumerateArray()
                .Single(c => c.GetProperty("kind").GetString() == kind)
                .GetProperty("value");
            float[] actual;
            if (image is RenderTexture)
                actual = value
                    .GetProperty("rect")
                    .EnumerateArray()
                    .Concat(value.GetProperty("uv").EnumerateArray())
                    .Select(n => n.GetSingle())
                    .ToArray();
            else
                actual = value
                    .GetProperty("quads")
                    .EnumerateArray()
                    .Select(n => n.GetSingle())
                    .ToArray();
            float[] expected =
            [
                destination.x,
                destination.y,
                destination.width,
                destination.height,
                uv.x,
                uv.y,
                uv.width,
                uv.height,
            ];
            if (
                actual.Length != expected.Length
                || actual.Where((n, i) => Math.Abs(n - expected[i]) > .00001f).Any()
            )
                throw new Exception(
                    $"Wrong {mode} geometry for {image.width}x{image.height}: [{string.Join(", ", actual)}]"
                );
            float[] colors = value
                .GetProperty("color")
                .EnumerateArray()
                .Select(n => n.GetSingle())
                .ToArray();
            if (colors.Length != 4 || Math.Abs(colors[3] - 128f / 255) > .00001f)
                throw new Exception($"Texture {mode} lost tint alpha");
        }
    }

    static void Paint(BrowserGui gui, Texture2D texture) =>
        gui.DrawTextureWithTexCoords(
            new Rect(1.25f, 2.5f, 20, 30),
            texture,
            new Rect(.1f, .2f, .7f, .8f),
            new Color(.3f, .6f, .9f, .5f)
        );
}
