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
        ImageVersion Identity,
        int Version,
        int Width,
        int Height,
        object? Data
    );

    static readonly ConditionalWeakTable<Texture2D, ImageVersion> imageVersions = new();
    static readonly ConditionalWeakTable<Texture2D, ImageVersion> appendOnlyAtlases = new();
    readonly Dictionary<int, (int CommandIndex, ImageVersion Identity)> pendingAtlasImages = new();

    // Existing glyph texels never change within one atlas texture. A later upload
    // can replace its earlier packet definition without changing earlier draws.
    internal static void RegisterGlyphAtlas(Texture2D texture) =>
        appendOnlyAtlases.GetOrCreateValue(texture);

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
        int id = texture.GetInstanceID();
        var identity = imageVersions.GetOrCreateValue(texture);
        int version = identity.Value;
        if (
            images.TryGetValue(id, out var previous)
            // The per-texture version object also protects against recycled IDs,
            // without another weak-reference lookup on every cell.
            && ReferenceEquals(previous.Identity, identity)
            && previous.Version == version
            && previous.Width == texture.width
            && previous.Height == texture.height
            && ReferenceEquals(previous.Data, texture.CoreTexture.CpuData)
        )
        {
            if (recordingRow is { Valid: true })
            {
                if (pendingImages.Contains(id))
                    RejectRow();
                else if (!recordingRow.Images.ContainsKey(id))
                {
                    // The recorder must not prolong the upload state's CPU pixel
                    // backing after resource collection removes its live owner.
                    recordingRow.Images.Add(id, new WeakReference<ImageState>(previous));
                    recordingRow.Bytes += 64;
                }
            }
            return;
        }
        RejectRow();
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
        var image = new ImageCommand(
            id,
            texture.width,
            texture.height,
            Convert.ToBase64String(bytes)
        );
        bool appendOnly = appendOnlyAtlases.TryGetValue(texture, out _);
        if (
            appendOnly
            && !SuppressDrawing
            && pendingAtlasImages.TryGetValue(id, out var pending)
            && ReferenceEquals(pending.Identity, identity)
        )
            commands[pending.CommandIndex] = new Command("image", image);
        else
        {
            int before = commands.Count;
            Emit("image", image);
            if (appendOnly && commands.Count == before + 1)
                pendingAtlasImages[id] = (before, identity);
        }
        images[id] = new ImageState(
            new WeakReference<Texture2D>(texture),
            identity,
            version,
            texture.width,
            texture.height,
            texture.CoreTexture.CpuData
        );
        pendingImages.Add(id);
    }
}
