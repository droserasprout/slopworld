// Font identity, local glyph coverage, metrics and atlas resource ownership. Neither font
// selection nor terminal cell policy belongs here; the actual mod retains both.
using System.Text.Json;
using UnityEngine;
using UnityStubs.Gui;

namespace BrowserAdapters;

public sealed partial class BrowserFontBank : ITextMeshProBackend
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
        internal uint LoaderFlags;
        internal readonly HashSet<int> Substituted = new();
        internal readonly HashSet<int> Unsupported = new();
        internal readonly HashSet<int> Failed = new();
        internal Color32[]? Pixels;
        internal int CursorX = 1,
            CursorY = 1,
            RowHeight;

        internal void InvalidateRuns() => textRuns = null;

        public readonly Dictionary<int, TmpGlyphMetrics> Glyphs = new();

        // Keep layout available while a supplementary glyph is with the worker.
        // GlyphsReady discards the provisional run; the real scalar stays retryable.
        internal bool TryTextGlyph(int scalar, out TmpGlyphMetrics glyph) =>
            Glyphs.TryGetValue(scalar, out glyph)
            || (scalar > char.MaxValue && Glyphs.TryGetValue(0, out glyph));

        BrowserTextCache<TmpGlyphMetrics>? textRuns;
        internal BrowserTextCache<TmpGlyphMetrics> TextRuns =>
            textRuns ??= new(
                TryTextGlyph,
                glyph => glyph.advanceX,
                glyph => glyph.x0,
                glyph =>
                    float.IsFinite(glyph.x1)
                    && glyph.x1 >= glyph.x0
                    && float.IsFinite(glyph.x1 - glyph.x0)
                    && float.IsFinite(glyph.y0)
                    && float.IsFinite(glyph.y1)
                    && glyph.y1 >= glyph.y0
                    && float.IsFinite(glyph.y1 - glyph.y0),
                glyph =>
                    Math.Max(
                        Math.Abs(glyph.advanceX),
                        Math.Max(
                            Math.Max(Math.Abs(glyph.x0), Math.Abs(glyph.x1)),
                            Math.Max(Math.Abs(glyph.y0), Math.Abs(glyph.y1))
                        )
                    )
            );
    }

    public static readonly BrowserFontBank Instance = new();
    readonly Dictionary<int, Face> faces = new();
    readonly List<Face> variants = new();
    float density = 1;
    readonly Dictionary<string, string> paths = new();
    static Dictionary<string, string[]> familyChains = new(StringComparer.OrdinalIgnoreCase);
    static string mountRoot = "/BrowserFonts";
    int nextIndex = 3;
    long atlasBytes;
    const long AtlasBudget = 384L * 1024 * 1024;

    static bool Capacity(int bytes) => bytes >= 0 && Instance.atlasBytes + bytes <= AtlasBudget;

    public float BakePixelSize => faces.Values.FirstOrDefault()?.Size ?? 0;
    public int AtlasVersion { get; private set; }
    public int ResolutionVersion { get; private set; }

    public static void ConfigureDensity(float value)
    {
        if (!float.IsFinite(value) || value < .5f || value > 4)
            throw new ArgumentOutOfRangeException(nameof(value));
        if (Instance.density == value)
            return;
        Instance.density = value;
        foreach (var face in Instance.variants)
            RetireLocalFace(face);
        Instance.variants.Clear();
        Instance.atlasBytes = 0;
        Instance.glyphEntries = 0;
        Instance.ResolutionVersion++;
        Instance.AtlasVersion++;
    }

    public static string[] Paths() => Instance.paths.Values.ToArray();

    public static string Snapshot() =>
        JsonSerializer.Serialize(
            new
            {
                density = Instance.density,
                atlasVersion = Instance.AtlasVersion,
                failedGlyphs = Instance.variants.Sum(face => face.Failed.Count),
                unsupportedGlyphs = Instance.variants.Sum(face => face.Unsupported.Count),
                glyphCacheFull = Instance.glyphEntries >= GlyphBudget,
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

    public static void ObserveText(int index, float size, FontStyle style, string text)
    {
        if (TryFace(index, size, style, out var face))
            EnsureGlyphs(face, text);
    }

    public static bool TryFace(int index, float size, out Face face) =>
        TryFace(index, size, FontStyle.Normal, out face);

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
        if (!face.Substituted.Contains(c) && face.Glyphs.TryGetValue(c, out glyph))
            return true;
        EnsureGlyphs(face, [c]);
        return !face.Substituted.Contains(c) && face.Glyphs.TryGetValue(c, out glyph);
    }

    public string GetCharacters(int index) =>
        TryFace(index, 0, out var face)
            ? string.Concat(face.Glyphs.Keys.Select(char.ConvertFromUtf32))
            : "";

    public Texture2D GetAtlasTexture(int index) =>
        TryFace(index, 0, out var face) ? face.Texture : null!;
}
