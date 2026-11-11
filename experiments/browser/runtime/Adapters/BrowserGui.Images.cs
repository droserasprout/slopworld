// Image upload lifetime follows Unity Apply/LoadImage and native texture identity.
// Draw packets reference uploaded pixels; mutable textures invalidate their upload.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Texture2D = UnityEngine.Texture2D;

namespace BrowserAdapters;

public sealed partial class BrowserGui
{
    sealed class ImageVersion
    {
        public int Value;
    }

    sealed record ImageState(
        WeakReference<Texture2D> Texture,
        int Version,
        int Width,
        int Height,
        object? Data
    );

    static readonly ConditionalWeakTable<Texture2D, ImageVersion> imageVersions = new();
    readonly Dictionary<int, ImageState> images = new();
    readonly HashSet<int> pendingImages = new();

    public static void TextureApplied(Texture2D texture) =>
        imageVersions.GetOrCreateValue(texture).Value++;

    void CollectImages()
    {
        foreach (var pair in images.ToArray())
            if (!pair.Value.Texture.TryGetTarget(out var texture) || texture == null)
            {
                Emit("releaseImage", pair.Key);
                images.Remove(pair.Key);
            }
    }

    void UploadImage(Texture2D texture)
    {
        int id = texture.GetInstanceID(),
            version = imageVersions.GetOrCreateValue(texture).Value;
        if (
            images.TryGetValue(id, out var previous)
            && previous.Version == version
            && previous.Width == texture.width
            && previous.Height == texture.height
            && ReferenceEquals(previous.Data, texture.CoreTexture.CpuData)
        )
            return;
        var pixels = texture.GetPixels32();
        var source = MemoryMarshal.AsBytes(pixels.AsSpan());
        var bytes = new byte[source.Length];
        int stride = texture.width * 4;
        // Unity reads bottom-up; Canvas consumes top-down. Bulk copies avoid the
        // cost of interpreting a managed operation for every channel of an atlas.
        for (int row = 0; row < texture.height; row++)
            source
                .Slice((texture.height - row - 1) * stride, stride)
                .CopyTo(bytes.AsSpan(row * stride));
        Emit(
            "image",
            new ImageCommand(id, texture.width, texture.height, Convert.ToBase64String(bytes))
        );
        images[id] = new ImageState(
            new WeakReference<Texture2D>(texture),
            version,
            texture.width,
            texture.height,
            texture.CoreTexture.CpuData
        );
        pendingImages.Add(id);
    }
}
