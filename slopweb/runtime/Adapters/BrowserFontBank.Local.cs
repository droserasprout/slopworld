// Local outline rasterization is synchronous once a font is installed. This owner
// commits glyphs and atlas pixels together; the host only selects/delivers fonts.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using UnityEngine;
using UnityStubs.Gui;

namespace BrowserAdapters;

public sealed partial class BrowserFontBank
{
    public static Func<string, string>? Rasterize;
    const int GlyphBudget = 262144;
    int glyphEntries;

    sealed record Source(byte[] Bytes, uint Flags, float Ascender, float Descender, float Spacing);

    readonly Dictionary<string, Source> sources = new();

    static int InstallLocal(byte[] bytes, string json, int index)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        float size = root.GetProperty("size").GetSingle();
        if (
            index >= 0
            && Instance.faces.TryGetValue(index, out var old)
            && (old.Hash != hash || old.Size != size)
        )
            throw new InvalidOperationException("Font index already assigned");
        if (!Instance.sources.ContainsKey(hash))
        {
            uint flags = root.GetProperty("loaderFlags").GetUInt32() & ~96u;
            var source = ReadSource(bytes, flags);
            Rasterize!(
                JsonSerializer.Serialize(new { hash, font = Convert.ToBase64String(bytes) })
            );
            string path = Path.Combine(mountRoot, hash + ".ttf");
            Directory.CreateDirectory(mountRoot);
            File.WriteAllBytes(path, bytes);
            Instance.sources.Add(hash, source);
            Instance.paths[hash] = path;
        }
        if (index < 0)
            index = RegisterLocal(bytes, size);
        else if (!Instance.faces.ContainsKey(index))
        {
            if (Instance.faces.Count >= 1024)
                throw new InvalidOperationException("Font registration limit reached");
            Instance.faces.Add(index, NewLocalFace(hash, size, FontStyle.Normal));
        }
        TextMeshProBackend.Current = Instance;
        Instance.ResolutionVersion++;
        return index;
    }

    static Source ReadSource(byte[] bytes, uint flags)
    {
        // Match installed FaceMetrics: head.unitsPerEm and hhea's design metrics.
        var data = bytes.AsSpan();
        int count = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4)),
            head = -1,
            hhea = -1;
        for (int i = 0; i < count; i++)
        {
            var table = data.Slice(12 + i * 16, 16);
            int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(table.Slice(8)));
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(table);
            if (tag == 0x68656164)
                head = offset;
            if (tag == 0x68686561)
                hhea = offset;
        }
        if (head < 0 || hhea < 0)
            throw new InvalidDataException("Missing font metrics");
        float units = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(head + 18));
        if (units == 0)
            throw new InvalidDataException("Missing font units");
        float ascender = BinaryPrimitives.ReadInt16BigEndian(data.Slice(hhea + 4));
        float descender = BinaryPrimitives.ReadInt16BigEndian(data.Slice(hhea + 6));
        float gap = BinaryPrimitives.ReadInt16BigEndian(data.Slice(hhea + 8));
        return new(
            bytes,
            flags,
            ascender / units,
            descender / units,
            (ascender - descender + gap) / units
        );
    }

    static int RegisterLocal(byte[] bytes, float size)
    {
        int existing = Register(bytes, size);
        if (existing >= 0)
            return existing;
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!Instance.sources.ContainsKey(hash))
            return -1;
        if (Instance.faces.Count >= 1024)
            throw new InvalidOperationException("Font registration limit reached");
        var face = NewLocalFace(hash, size, FontStyle.Normal);
        int index = Instance.nextIndex++;
        Instance.faces.Add(index, face);
        return index;
    }

    static Face NewLocalFace(string hash, float size, FontStyle style)
    {
        if (!float.IsFinite(size) || size <= 0 || size > 128)
            throw new ArgumentOutOfRangeException(nameof(size));
        if (Instance.variants.Count >= 1024 || !Capacity(256 * 256 * 4))
            throw new InvalidOperationException("Local font atlas limit reached");
        var source = Instance.sources[hash];
        uint flags =
            source.Flags | (((int)style & 1) != 0 ? 32u : 0u) | (((int)style & 2) != 0 ? 64u : 0u);
        using var metrics = JsonDocument.Parse(
            Rasterize!(
                JsonSerializer.Serialize(
                    new
                    {
                        hash,
                        size,
                        density = Instance.density,
                        flags,
                    }
                )
            )
        );
        float Snap(float value) => MathF.Floor(value + .5f);
        var face = new Face
        {
            Hash = hash,
            Size = size,
            Density = Instance.density,
            Style = style,
            LoaderFlags = flags,
            Ascent = metrics.RootElement.GetProperty("ascent").GetSingle(),
            Descent = metrics.RootElement.GetProperty("descent").GetSingle(),
            LineAscent = Snap(source.Ascender * size),
            FirstLine = Snap(source.Ascender * size) - Snap(source.Descender * size),
            Pitch = Snap(source.Spacing * size),
            Spacing = source.Spacing * size,
            Texture = new Texture2D(256, 256, TextureFormat.RGBA32, false),
            Pixels = new Color32[256 * 256],
        };
        Instance.atlasBytes += 256 * 256 * 4;
        BrowserGui.RegisterGlyphAtlas(face.Texture);
        Instance.variants.Add(face);
        Instance.AtlasVersion++;
        return face;
    }

    static bool TryLocalFace(int index, float size, FontStyle style, out Face face)
    {
        face = null!;
        if (!Instance.faces.TryGetValue(index, out var basis))
            return false;
        size = size <= 0 ? basis.Size : size;
        face =
            Instance.variants.FirstOrDefault(f =>
                f.Hash == basis.Hash
                && f.Size == size
                && f.Style == style
                && f.Density == Instance.density
            ) ?? NewLocalFace(basis.Hash, size, style);
        return true;
    }

    static void EnsureGlyphs(Face face, string text)
    {
        bool changed = false;
        try
        {
            foreach (char c in text)
            {
                if (
                    c is '\r' or '\n'
                    || char.IsSurrogate(c)
                    || face.Glyphs.ContainsKey(c)
                    || face.Unsupported.Contains(c)
                    || face.Failed.Contains(c)
                )
                    continue;
                // Count coverage misses and failures too: sparse font coverage
                // must not grow dictionaries independently of the pixel budget.
                if (Instance.glyphEntries >= GlyphBudget)
                    break;
                Instance.glyphEntries++;
                string result;
                try
                {
                    result = Rasterize!(
                        JsonSerializer.Serialize(
                            new
                            {
                                hash = face.Hash,
                                scalar = (int)c,
                                size = face.Size,
                                density = face.Density,
                                flags = face.LoaderFlags,
                            }
                        )
                    );
                }
                catch
                {
                    face.Failed.Add(c);
                    continue;
                }
                using var document = JsonDocument.Parse(result);
                var glyph = document.RootElement;
                if (glyph.TryGetProperty("missing", out _))
                {
                    face.Unsupported.Add(c);
                    continue;
                }
                int width = glyph.GetProperty("width").GetInt32(),
                    height = glyph.GetProperty("height").GetInt32();
                byte[] alpha = Convert.FromBase64String(glyph.GetProperty("pixels").GetString()!);
                if (width < 0 || height < 0 || alpha.Length != checked(width * height))
                    throw new InvalidDataException("Invalid glyph pixels");
                int x,
                    y;
                try
                {
                    Place(face, width, height, out x, out y);
                }
                catch (InvalidOperationException)
                {
                    face.Failed.Add(c);
                    continue;
                }
                int edge = face.Texture.width;
                for (int row = 0; row < height; row++)
                for (int col = 0; col < width; col++)
                    face.Pixels![(edge - y - row - 1) * edge + x + col] = new Color32(
                        255,
                        255,
                        255,
                        alpha[row * width + col]
                    );
                float x0 = glyph.GetProperty("x").GetSingle();
                face.Ascent = glyph.GetProperty("ascent").GetSingle();
                face.Descent = glyph.GetProperty("descent").GetSingle();
                float y0 = glyph.GetProperty("y").GetSingle() + face.Ascent;
                face.Glyphs.Add(
                    c,
                    new TmpGlyphMetrics
                    {
                        found = true,
                        visible = width > 0 && height > 0,
                        advanceX = glyph.GetProperty("advance").GetSingle(),
                        x0 = x0,
                        y0 = y0,
                        x1 = x0 + width / face.Density,
                        y1 = y0 + height / face.Density,
                        u0 = (float)x / edge,
                        v0 = (float)y / edge,
                        u1 = (float)(x + width) / edge,
                        v1 = (float)(y + height) / edge,
                    }
                );
                changed = true;
            }
        }
        finally
        {
            if (changed)
            {
                face.Texture.SetPixels32(face.Pixels!);
                face.Texture.Apply(false, false);
                face.InvalidateRuns();
                Instance.AtlasVersion++;
            }
        }
    }

    public static void RetryRasterization()
    {
        foreach (var face in Instance.variants)
        {
            Instance.glyphEntries -= face.Failed.Count;
            face.Failed.Clear();
        }
    }

    static void Place(Face face, int width, int height, out int x, out int y)
    {
        int edge = face.Texture.width,
            next = edge;
        x = face.CursorX;
        y = face.CursorY;
        int rowHeight = face.RowHeight;
        if (x + width + 1 > edge)
        {
            x = 1;
            y += rowHeight + 1;
            rowHeight = 0;
        }
        while (y + height + 1 > next || width + 2 > next)
        {
            next *= 2;
            if (next > 4096)
                throw new InvalidOperationException("Local glyph atlas is full");
        }
        if (next != edge)
        {
            int allocation = checked((next * next - edge * edge) * 4);
            if (!Capacity(allocation))
                throw new InvalidOperationException("Local glyph atlas budget reached");
            var pixels = new Color32[next * next];
            for (int row = 0; row < edge; row++)
                Array.Copy(face.Pixels!, row * edge, pixels, (next - edge + row) * next, edge);
            var texture = new Texture2D(next, next, TextureFormat.RGBA32, false);
            BrowserGui.RegisterGlyphAtlas(texture);
            UnityEngine.Object.Destroy(face.Texture);
            face.Texture = texture;
            face.Pixels = pixels;
            float ratio = (float)edge / next;
            foreach (char key in face.Glyphs.Keys.ToArray())
            {
                var glyph = face.Glyphs[key];
                glyph.u0 *= ratio;
                glyph.u1 *= ratio;
                glyph.v0 *= ratio;
                glyph.v1 *= ratio;
                face.Glyphs[key] = glyph;
            }
            Instance.atlasBytes += allocation;
        }
        face.CursorX = x + width + 1;
        face.CursorY = y;
        face.RowHeight = Math.Max(rowHeight, height);
    }

    public static void DisposeFonts()
    {
        foreach (var face in Instance.variants)
            RetireLocalFace(face);
        foreach (string path in Instance.paths.Values)
            File.Delete(path);
        Instance.variants.Clear();
        Instance.faces.Clear();
        Instance.sources.Clear();
        Instance.paths.Clear();
        Instance.resolved.Clear();
        Instance.missing.Clear();
        Instance.pendingSizes.Clear();
        Instance.atlasBytes = 0;
        Instance.glyphEntries = 0;
        Instance.nextIndex = 3;
        Instance.AtlasVersion++;
        Instance.ResolutionVersion++;
        Rasterize?.Invoke("{\"close\":true}");
    }

    static void RetireLocalFace(Face face)
    {
        // Registered indices retain only identity/size after a density change.
        // Do not let those stable handles retain retired CPU atlas or glyph data.
        UnityEngine.Object.Destroy(face.Texture);
        face.Texture = null!;
        face.Pixels = null;
        face.Glyphs.Clear();
        face.Glyphs.TrimExcess();
        face.Unsupported.Clear();
        face.Unsupported.TrimExcess();
        face.Failed.Clear();
        face.Failed.TrimExcess();
        face.InvalidateRuns();
    }
}
