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
    readonly HashSet<(int, float, FontStyle)> missing = new();
    float density = 1;
    readonly Dictionary<string, string> paths = new();
    static Dictionary<string, string[]> familyChains = new(StringComparer.OrdinalIgnoreCase);
    static string mountRoot = "/BrowserFonts";
    int nextIndex = 3;
    long atlasBytes;
    const long AtlasBudget = 384L * 1024 * 1024;

    public static bool Capacity(int bytes) =>
        bytes >= 0 && Instance.atlasBytes + bytes <= AtlasBudget;

    readonly HashSet<(int, float)> pendingSizes = new();
    public float BakePixelSize => 16;
    public int AtlasVersion { get; private set; }

    public static void ConfigureDensity(float value)
    {
        if (Instance.density == value)
            return;
        Instance.density = value;
        Instance.resolved.Clear();
        Instance.missing.Clear();
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

    public static void InstallChains(string json)
    {
        var chains =
            JsonSerializer.Deserialize<Dictionary<string, string[]>>(json)
            ?? throw new InvalidDataException("Missing host font families");
        foreach (var pair in chains)
            familyChains[pair.Key] = pair.Value;
    }

    public static string[] Families(string family) =>
        familyChains.TryGetValue(family, out var chain) ? (string[])chain.Clone()
        : familyChains.Values.FirstOrDefault() is { } previous ? (string[])previous.Clone()
        : [family];

    static string[] catalog = [],
        mono = [];

    public static void InstallCatalog(string json)
    {
        using var document = JsonDocument.Parse(json);
        catalog = document.RootElement.GetProperty("all").Deserialize<string[]>()!;
        mono = document.RootElement.GetProperty("mono").Deserialize<string[]>()!;
    }

    public static bool HasCatalog() => catalog.Length > 0;

    public static string[] Names(bool monospaced) =>
        (string[])(monospaced ? mono : catalog).Clone();

    // A changed size can temporarily reuse a mounted face while its exact native
    // bake is prepared. Installation itself always uses exact identity matching.
    public static int RegisterAvailable(byte[] bytes, float size)
    {
        int exact = Register(bytes, size);
        if (exact >= 0)
            return exact;
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        foreach (var pair in Instance.faces)
            if (pair.Value.Hash == hash)
            {
                if (Instance.pendingSizes.Count >= 1024)
                    Instance.pendingSizes.Clear();
                Instance.pendingSizes.Add((pair.Key, size));
                Instance.resolved.Clear();
                Instance.missing.Clear();
                return pair.Key;
            }
        return -1;
    }

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
        string identity = Convert.ToHexString(SHA256.HashData(font));
        float bakedSize = root.GetProperty("size").GetSingle();
        float bakedDensity = root.GetProperty("density").GetSingle();
        var bakedStyle = root.TryGetProperty("style", out var bakedStyleValue)
            ? (FontStyle)bakedStyleValue.GetInt32()
            : FontStyle.Normal;
        if (
            Instance.variants.Any(old =>
                old.Hash == identity
                && old.Size == bakedSize
                && old.Style == bakedStyle
                && old.Density == bakedDensity
            )
        )
            return defaultIndex >= 0 ? defaultIndex : Register(font, bakedSize);
        if (
            defaultIndex >= 0
            && Instance.faces.TryGetValue(defaultIndex, out var indexed)
            && (indexed.Hash != identity || Math.Abs(indexed.Size - bakedSize) >= .001f)
        )
            throw new InvalidOperationException("Font index already installed with another face");
        int allocation = checked(width * height * 4);
        if (!Capacity(allocation) || Instance.variants.Count >= 1024)
            throw new InvalidOperationException("Native font atlas memory limit reached");
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
        string hash = identity;
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
        string path = Path.Combine(mountRoot, hash + ".ttf");
        try
        {
            if (!Instance.paths.ContainsKey(hash))
            {
                Directory.CreateDirectory(mountRoot);
                File.WriteAllBytes(path, font);
            }
        }
        catch
        {
            UnityEngine.Object.Destroy(texture);
            throw;
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
        Instance.atlasBytes += allocation;
        Instance.resolved.Clear();
        Instance.missing.Clear();
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
        if (Instance.missing.Contains((index, size, style)))
            return false;
        if (Instance.faces.TryGetValue(index, out var basis))
        {
            float targetSize = size <= 0 ? basis.Size : size;
            if (
                Instance.pendingSizes.Contains((index, targetSize))
                && !Instance.variants.Any(candidate =>
                    candidate.Hash == basis.Hash
                    && candidate.Size == targetSize
                    && candidate.Style == style
                )
            )
                targetSize = basis.Size;
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
        if (Instance.missing.Count >= 256)
            Instance.missing.Clear();
        Instance.missing.Add((index, size, style));
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
