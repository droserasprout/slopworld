// Image upload lifetime follows Unity Apply/LoadImage and native texture identity.
// Draw packets reference uploaded pixels; mutable textures invalidate their upload.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Color32 = UnityEngine.Color32;
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

    sealed class GlyphAtlas
    {
        public Color32[]? Pixels;
        public bool Dirty;
    }

    sealed record PendingAtlasImage(
        Texture2D Texture,
        ImageVersion Identity,
        GlyphAtlas Atlas,
        int Id,
        int Width,
        int Height,
        Color32[] Pixels
    );

    static readonly ConditionalWeakTable<Texture2D, GlyphAtlas> appendOnlyAtlases = new();
    readonly Dictionary<int, (int CommandIndex, ImageVersion Identity)> pendingAtlasImages = new();

    // Existing glyph texels never change within one atlas texture. A later upload
    // can replace its earlier packet definition without changing earlier draws.
    internal static void RegisterGlyphAtlas(Texture2D texture) =>
        appendOnlyAtlases.GetOrCreateValue(texture);

    // The font bank owns append-only pixels. Keep its current array until drain;
    // atlas growth registers a new texture/array, preserving earlier packet draws.
    internal static void UpdateGlyphAtlas(Texture2D texture, Color32[] pixels)
    {
        var atlas = appendOnlyAtlases.GetOrCreateValue(texture);
        atlas.Pixels = pixels;
        atlas.Dirty = true;
        TextureApplied(texture);
    }

    readonly Dictionary<int, ImageState> images = new();
    readonly HashSet<int> pendingImages = new();

    public static void TextureApplied(Texture2D texture) =>
        imageVersions.GetOrCreateValue(texture).Value++;

    static object? ImageBacking(Texture2D texture) =>
        appendOnlyAtlases.TryGetValue(texture, out var atlas) && atlas.Pixels != null
            ? atlas.Pixels
            : texture.CoreTexture.CpuData;

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
        appendOnlyAtlases.TryGetValue(texture, out var atlas);
        object? data = atlas?.Pixels ?? (object?)texture.CoreTexture.CpuData;
        if (
            images.TryGetValue(id, out var previous)
            // The per-texture version object also protects against recycled IDs,
            // without another weak-reference lookup on every cell.
            && ReferenceEquals(previous.Identity, identity)
            && previous.Version == version
            && previous.Width == texture.width
            && previous.Height == texture.height
            && ReferenceEquals(previous.Data, data)
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
        bool appendOnly = atlas != null;
        object image = appendOnly
            ? new PendingAtlasImage(
                texture,
                identity,
                atlas!,
                id,
                texture.width,
                texture.height,
                atlas!.Pixels ?? texture.GetPixels32()
            )
            : EncodeImage(id, texture.width, texture.height, texture.GetPixels32());
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
            data
        );
        pendingImages.Add(id);
    }

    static ImageCommand EncodeImage(int id, int width, int height, Color32[] pixels)
    {
        var source = MemoryMarshal.AsBytes(pixels.AsSpan());
        var bytes = new byte[source.Length];
        int stride = width * 4;
        for (int row = 0; row < height; row++)
            source.Slice((height - row - 1) * stride, stride).CopyTo(bytes.AsSpan(row * stride));
        return new ImageCommand(id, width, height, Convert.ToBase64String(bytes));
    }

    void FlushAtlasImages()
    {
        for (int index = 0; index < commands.Count; index++)
        {
            if (commands[index].value is not PendingAtlasImage pending)
                continue;
            // Native texture backing is published once for the retained version.
            // Destroyed old atlases still have packet-owned pixels for earlier draws.
            if (pending.Texture != null && pending.Atlas.Dirty)
            {
                pending.Texture.SetPixels32(pending.Pixels);
                pending.Texture.Apply(false, false);
                pending.Atlas.Dirty = false;
                if (
                    images.TryGetValue(pending.Id, out var state)
                    && ReferenceEquals(state.Identity, pending.Identity)
                )
                    images[pending.Id] = state with { Version = pending.Identity.Value };
            }
            commands[index] = new Command(
                "image",
                EncodeImage(pending.Id, pending.Width, pending.Height, pending.Pixels)
            );
        }
    }
}
