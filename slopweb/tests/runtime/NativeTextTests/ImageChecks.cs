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
        CheckPacketIdentityAcrossInstall();
        CheckRestoredTarget();
        CheckScaleModes();
        CheckDeferredAtlas();
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
        // Both an updated image and a dead target already exist on Canvas. Their
        // releases must survive a writer failure, including rollback of the upload.
        using var target = new RenderTexture(2, 2, 0);
        reference.ActiveRenderTextureChanged(target);
        reference.ActiveRenderTextureChanged(null!);
        Drain(0, "published target");
        BrowserGui.TextureApplied(fresh);
        reference.DrawTextureWithTexCoords(
            new Rect(float.NaN, 0, 2, 2),
            fresh,
            new Rect(0, 0, 1, 1),
            Color.white
        );
        UnityEngine.Object.DestroyImmediate(fresh);
        target.Release();
        try
        {
            reference.Drain();
            throw new Exception("Expected failed drain before release commit");
        }
        catch (ArgumentException) { }
        using var retry = JsonDocument.Parse(Drain(0, "retry destroyed resource releases"));
        var releases = retry
            .RootElement.EnumerateArray()
            .Select(c => c.GetProperty("kind").GetString())
            .ToArray();
        if (!releases.SequenceEqual(new[] { "releaseImage", "releaseTarget" }))
            throw new Exception("Retry did not release both published resources");
        if (Drain(0, "committed releases") != "[]")
            throw new Exception("Successful retry did not commit resource cleanup");
        Console.WriteLine(
            "Image uploads track Apply, CPU backing, dimensions, switches, discard, ID reuse and disposal."
        );
    }

    static void CheckRestoredTarget()
    {
        var gui = new BrowserGui();
        using var pane = new RenderTexture(8, 4, 0);
        void Draw()
        {
            if (Event.current.type != EventType.Repaint)
                return;
            // Use the terminal pane's native freshness check.
            if (!pane.IsCreated())
            {
                gui.ActiveRenderTextureChanged(pane);
                gui.FillRect(new Rect(0, 0, 8, 4), Color.red);
                gui.ActiveRenderTextureChanged(null!);
            }
            gui.DrawTexture(new Rect(0, 0, 8, 4), pane, ScaleMode.StretchToFill, Color.white);
        }
        foreach (
            var (batch, repaint) in new[]
            {
                ("[]", true),
                ("[]", false),
                ("""[{"kind":"restoreTargets"}]""", true),
                ("[]", false),
            }
        )
        {
            using var result = JsonDocument.Parse(BrowserGuiInput.Dispatch(batch, gui, Draw));
            var kinds = result
                .RootElement.GetProperty("commands")
                .EnumerateArray()
                .Select(command => command.GetProperty("kind").GetString())
                .ToArray();
            if (!kinds.Contains("targetTexture") || kinds.Contains("fill") != repaint)
                throw new Exception("Restored terminal target did not repaint once before reuse");
        }
    }

    static void CheckPacketIdentityAcrossInstall()
    {
        var previousGui = UnityStubs.Gui.GuiBackend.Current;
        bool previous = BrowserGui.ConfigurePacketCache(true);
        try
        {
            var first = BrowserGui.Install();
            first.Drain();
            long acknowledged = first.PacketId;
            var replacement = BrowserGui.Install();
            replacement.Drain();
            if (acknowledged == 0 || replacement.PacketId <= acknowledged)
                throw new Exception("Replacement GUI reused a previous backend's packet identity");
        }
        finally
        {
            BrowserGui.ConfigurePacketCache(previous);
            UnityStubs.Gui.GuiBackend.Current = previousGui;
        }
    }

    static void CheckDeferredAtlas()
    {
        var publish = typeof(BrowserGui).GetMethod(
            "UpdateGlyphAtlas",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        var gui = new BrowserGui();
        using var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Color32[] pixels = [Color.red, Color.clear, Color.clear, Color.clear];
        var backing = texture.CoreTexture.CpuData;
        void Publish() => publish.Invoke(null, new object[] { texture, pixels });
        Publish();
        Paint(gui, texture);
        pixels[1] = Color.green;
        Publish();
        Paint(gui, texture);
        if (!ReferenceEquals(backing, texture.CoreTexture.CpuData))
            throw new Exception("Glyph additions published backing before drain");
        using (var packet = JsonDocument.Parse(gui.Drain()))
        {
            var image = packet
                .RootElement.EnumerateArray()
                .Single(c => c.GetProperty("kind").GetString() == "image")
                .GetProperty("value");
            byte[] bytes = Convert.FromBase64String(image.GetProperty("pixels").GetString()!);
            if (bytes.Length != 16 || bytes[8] != 255 || bytes[12 + 1] != 255)
                throw new Exception("Coalesced atlas lost glyph pixels or row orientation");
        }
        backing = texture.CoreTexture.CpuData;
        pixels[2] = Color.blue;
        Publish();
        Paint(gui, texture);
        gui.Discard();
        if (!ReferenceEquals(backing, texture.CoreTexture.CpuData))
            throw new Exception("Discarded atlas performed publication");
        Paint(gui, texture);
        using (var packet = JsonDocument.Parse(gui.Drain()))
            if (
                !packet
                    .RootElement.EnumerateArray()
                    .Any(c => c.GetProperty("kind").GetString() == "image")
            )
                throw new Exception("Discard lost pending atlas upload");
        Publish();
        gui.DrawTextureWithTexCoords(
            new Rect(float.NaN, 0, 2, 2),
            texture,
            new Rect(0, 0, 1, 1),
            Color.white
        );
        try
        {
            gui.Drain();
            throw new Exception("Non-finite packet unexpectedly serialized");
        }
        catch (ArgumentException) { }
        Paint(gui, texture);
        using (var packet = JsonDocument.Parse(gui.Drain()))
            if (
                !packet
                    .RootElement.EnumerateArray()
                    .Any(c => c.GetProperty("kind").GetString() == "image")
            )
                throw new Exception("Failed drain lost its resource definition");
        Publish();
        Paint(gui, texture);
        UnityEngine.Object.DestroyImmediate(texture);
        using (var packet = JsonDocument.Parse(gui.Drain()))
            if (
                !packet
                    .RootElement.EnumerateArray()
                    .Any(c => c.GetProperty("kind").GetString() == "image")
            )
                throw new Exception("Disposed atlas lost earlier packet-owned pixels");
        Console.WriteLine(
            "Atlas uploads defer publication, coalesce pixels, survive disposal, and recover after discard/failure."
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
