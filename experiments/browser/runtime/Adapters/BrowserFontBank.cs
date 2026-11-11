// Local native-bake data owns glyph coverage, metrics and pixels. Neither font
// selection nor terminal cell policy belongs here; the actual mod retains both.
using System.Security.Cryptography;
using System.Text.Json;
using UnityEngine;
using UnityStubs.Gui;

namespace BrowserAdapters;

public sealed class BrowserFontBank : ITextMeshProBackend
{
    public sealed class Face
    {
        public required string Hash;
        public FontStyle Style;
        public required float Size,
            Density,
            Ascent,
            LineAscent,
            Descent,
            FirstLine,
            Pitch,
            Spacing;
        public required Texture2D Texture;
        public readonly Dictionary<char, TmpGlyphMetrics> Glyphs = new();
        BrowserTextCache<TmpGlyphMetrics>? textRuns;
        internal BrowserTextCache<TmpGlyphMetrics> TextRuns =>
            textRuns ??= new(Glyphs, glyph => glyph.advanceX);
    }

    public static readonly BrowserFontBank Instance = new();
    readonly Dictionary<int, Face> faces = new();
    readonly List<Face> variants = new();
    readonly Dictionary<(int, float, FontStyle), Face> resolved = new();
    float density = 1;
    readonly Dictionary<string, string> paths = new();
    static Dictionary<string, string[]> familyChains = new(StringComparer.OrdinalIgnoreCase);
    int nextIndex = 3;
    public float BakePixelSize => 16;
    public int AtlasVersion { get; private set; }

    public static void ConfigureDensity(float value)
    {
        if (Instance.density == value)
            return;
        Instance.density = value;
        Instance.resolved.Clear();
        Instance.AtlasVersion++;
    }

    public static string[] Paths() => Instance.paths.Values.ToArray();

    public static string Snapshot() =>
        JsonSerializer.Serialize(
            new
            {
                density = Instance.density,
                atlasVersion = Instance.AtlasVersion,
                faces = Instance.faces.Select(pair =>
                {
                    TryFace(pair.Key, 0, out var selected);
                    return new
                    {
                        index = pair.Key,
                        size = selected.Size,
                        density = selected.Density,
                        texture = selected.Texture.GetInstanceID(),
                        available = Instance
                            .variants.Where(face =>
                                face.Hash == selected.Hash
                                && face.Size == selected.Size
                                && face.Style == FontStyle.Normal
                            )
                            .Select(face => face.Density),
                    };
                }),
            }
        );

    public static void InstallChains(string json) =>
        familyChains = new(
            JsonSerializer.Deserialize<Dictionary<string, string[]>>(json)
                ?? throw new InvalidDataException("Missing host font families"),
            StringComparer.OrdinalIgnoreCase
        );

    public static string[] Families(string family) =>
        familyChains.TryGetValue(family, out var chain)
            ? (string[])chain.Clone()
            : throw new InvalidOperationException(
                "Host font family has not been prepared: " + family
            );

    public static int Install(byte[] font, string json, int defaultIndex)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var atlas = root.GetProperty("atlas");
        int width = atlas.GetProperty("width").GetInt32(),
            height = atlas.GetProperty("height").GetInt32();
        int channels = atlas.GetProperty("bytesPerPixel").GetInt32();
        if (
            width <= 0
            || height <= 0
            || width > 8192
            || height > 8192
            || (channels != 1 && channels != 4)
        )
            throw new InvalidDataException("Unsupported font atlas");
        var data = Convert.FromBase64String(atlas.GetProperty("pixels").GetString()!);
        if (data.Length != checked(width * height * channels))
            throw new InvalidDataException("Truncated font atlas");
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int source = (y * width + x) * channels;
            pixels[(height - y - 1) * width + x] =
                channels == 1
                    ? new Color32(255, 255, 255, data[source])
                    : new Color32(
                        data[source],
                        data[source + 1],
                        data[source + 2],
                        data[source + 3]
                    );
        }
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        string hash = Convert.ToHexString(SHA256.HashData(font));
        var metrics = root.GetProperty("lineMetrics");
        var face = new Face
        {
            Hash = hash,
            Style = root.TryGetProperty("style", out var style)
                ? (FontStyle)style.GetInt32()
                : FontStyle.Normal,
            Size = root.GetProperty("size").GetSingle(),
            Density = root.GetProperty("density").GetSingle(),
            Ascent = root.GetProperty("ascent").GetSingle(),
            LineAscent = metrics.GetProperty("Ascent").GetSingle(),
            Descent = root.GetProperty("descent").GetSingle(),
            FirstLine = metrics.GetProperty("FirstLine").GetSingle(),
            Pitch = metrics.GetProperty("Pitch").GetSingle(),
            Spacing = metrics.GetProperty("Spacing").GetSingle(),
            Texture = texture,
        };
        foreach (var item in root.GetProperty("glyphs").EnumerateArray())
        {
            int scalar = item.GetProperty("scalar").GetInt32();
            if (scalar > char.MaxValue || item.GetProperty("fallback").GetBoolean())
                continue;
            face.Glyphs.Add(
                (char)scalar,
                new TmpGlyphMetrics
                {
                    found = true,
                    visible = item.GetProperty("visible").GetBoolean(),
                    advanceX = item.GetProperty("advance").GetSingle(),
                    x0 = item.GetProperty("x0").GetSingle(),
                    y0 = item.GetProperty("y0").GetSingle(),
                    x1 = item.GetProperty("x1").GetSingle(),
                    y1 = item.GetProperty("y1").GetSingle(),
                    u0 = item.GetProperty("u0").GetSingle(),
                    v0 = item.GetProperty("v0").GetSingle(),
                    u1 = item.GetProperty("u1").GetSingle(),
                    v1 = item.GetProperty("v1").GetSingle(),
                }
            );
        }
        int index = defaultIndex >= 0 ? defaultIndex : Register(font, face.Size);
        if (index < 0)
            index = Instance.nextIndex++;
        if (Instance.faces.TryGetValue(index, out var existing))
        {
            if (existing.Hash != hash || Math.Abs(existing.Size - face.Size) >= .001f)
                throw new InvalidOperationException(
                    "Font index already installed with another face"
                );
        }
        else
            Instance.faces.Add(index, face);
        Instance.variants.Add(face);
        Instance.resolved.Clear();
        string path = "/BrowserFonts/" + hash + ".ttf";
        Directory.CreateDirectory("/BrowserFonts");
        File.WriteAllBytes(path, font);
        Instance.paths[hash] = path;
        Instance.AtlasVersion++;
        TextMeshProBackend.Current = Instance;
        return index;
    }

    public static int Register(byte[] bytes, float size)
    {
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        foreach (var pair in Instance.faces)
            if (pair.Value.Hash == hash && Math.Abs(pair.Value.Size - size) < .001f)
                return pair.Key;
        return -1;
    }

    public static bool TryFace(int index, float size, out Face face) =>
        TryFace(index, size, FontStyle.Normal, out face);

    public static bool TryFace(int index, float size, FontStyle style, out Face face)
    {
        face = null!;
        if (Instance.resolved.TryGetValue((index, size, style), out face!))
            return true;
        if (Instance.faces.TryGetValue(index, out var basis))
        {
            float targetSize = size <= 0 ? basis.Size : size;
            float distance = float.PositiveInfinity;
            foreach (var candidate in Instance.variants)
                if (
                    candidate.Hash == basis.Hash
                    && candidate.Style == style
                    && Math.Abs(targetSize - candidate.Size) < .001f
                    && Math.Abs(Instance.density - candidate.Density) < distance
                )
                {
                    face = candidate;
                    distance = Math.Abs(Instance.density - candidate.Density);
                }
            if (face != null)
            {
                if (Instance.resolved.Count >= 256)
                    Instance.resolved.Clear();
                Instance.resolved[(index, size, style)] = face;
                return true;
            }
        }
        face = null!;
        return false;
    }

    public bool TryGetFontMetrics(int index, out TmpFontMetrics metrics)
    {
        metrics = default;
        if (!TryFace(index, 0, out var face))
            return false;
        metrics = new TmpFontMetrics
        {
            found = true,
            bakedPixelSize = face.Size,
            ascent = face.Ascent,
            descent = face.Descent,
            lineHeight = face.Spacing,
        };
        return true;
    }

    public bool TryGetGlyph(int index, char c, out TmpGlyphMetrics glyph) =>
        TryGetGlyphAtSize(index, c, 0, out glyph, out _);

    public bool TryGetGlyphAtSize(
        int index,
        char c,
        float size,
        out TmpGlyphMetrics glyph,
        out float ascent
    )
    {
        glyph = default;
        ascent = 0;
        if (!TryFace(index, size, out var face))
            return false;
        ascent = face.Ascent;
        return face.Glyphs.TryGetValue(c, out glyph);
    }

    public string GetCharacters(int index) =>
        TryFace(index, 0, out var face) ? new string(face.Glyphs.Keys.ToArray()) : "";

    public Texture2D GetAtlasTexture(int index) =>
        TryFace(index, 0, out var face) ? face.Texture : null!;
}
