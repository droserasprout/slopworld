// Coverage misses request host font files asynchronously. Resolved outlines are
// rasterized into the requesting face's atlas, preserving its baseline and APIs.
using System.Globalization;
using System.Text.Json;

namespace BrowserAdapters;

public sealed partial class BrowserFontBank
{
    readonly Dictionary<string, string> sourceFamilies = new();
    readonly Dictionary<(string Hash, int Scalar), string?> fallbackSources = new();
    readonly Dictionary<string, HashSet<int>> wantedFallbacks = new();
    readonly Dictionary<long, (string Hash, int[] Scalars)> requestedFallbacks = new();
    long nextFallbackRequest;

    static void WantFallback(string hash, int scalar)
    {
        var bank = Instance;
        if (
            !bank.sourceFamilies.ContainsKey(hash)
            || bank.fallbackSources.ContainsKey((hash, scalar))
        )
            return;
        if (bank.requestedFallbacks.Values.Any(r => r.Hash == hash && r.Scalars.Contains(scalar)))
            return;
        if (!bank.wantedFallbacks.TryGetValue(hash, out var wanted))
            bank.wantedFallbacks.Add(hash, wanted = new());
        if (wanted.Count < 256)
            wanted.Add(scalar);
    }

    public static string TakeFallbacks()
    {
        var bank = Instance;
        if (bank.requestedFallbacks.Count > 0 || bank.wantedFallbacks.Count == 0)
            return "";
        var pair = bank.wantedFallbacks.First();
        bank.wantedFallbacks.Remove(pair.Key);
        int[] scalars = pair.Value.Order().ToArray();
        long id = ++bank.nextFallbackRequest;
        bank.requestedFallbacks.Add(id, (pair.Key, scalars));
        return JsonSerializer.Serialize(
            new
            {
                id,
                family = bank.sourceFamilies[pair.Key],
                scalars,
            }
        );
    }

    public static bool CompleteFallbacks(string json)
    {
        var bank = Instance;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        long id = root.GetProperty("id").GetInt64();
        if (!bank.requestedFallbacks.TryGetValue(id, out var request))
            return false;
        var assignments = root.GetProperty("assignments");
        var resolved = new Dictionary<int, string?>();
        foreach (int scalar in request.Scalars)
        {
            string? hash = assignments
                .GetProperty(scalar.ToString(CultureInfo.InvariantCulture))
                .GetString();
            if (hash != null && !bank.sources.ContainsKey(hash))
                throw new InvalidDataException("Fallback font is not installed");
            resolved.Add(scalar, hash);
        }
        bank.requestedFallbacks.Remove(id);
        foreach (var pair in resolved)
            bank.fallbackSources[(request.Hash, pair.Key)] = pair.Value;
        foreach (var face in bank.variants.Where(f => f.Hash == request.Hash))
        {
            foreach (int scalar in request.Scalars)
            {
                char c = (char)scalar;
                if (face.Substituted.Remove(c))
                {
                    face.Glyphs.Remove(c);
                    bank.glyphEntries--;
                }
                if (face.Unsupported.Remove(c))
                    bank.glyphEntries--;
                if (face.Failed.Remove(c))
                    bank.glyphEntries--;
            }
            face.InvalidateRuns();
        }
        bank.ResolutionVersion++;
        bank.AtlasVersion++;
        return true;
    }

    public static void RetryFallbacks()
    {
        foreach (var request in Instance.requestedFallbacks.Values)
        {
            if (!Instance.wantedFallbacks.TryGetValue(request.Hash, out var wanted))
                Instance.wantedFallbacks.Add(request.Hash, wanted = new());
            wanted.UnionWith(request.Scalars);
        }
        Instance.requestedFallbacks.Clear();
    }

    static string RasterizeGlyph(Face face, char c, out bool substituted)
    {
        string Render(string hash, int scalar) =>
            Rasterize!(
                JsonSerializer.Serialize(
                    new
                    {
                        hash,
                        scalar,
                        size = face.Size,
                        density = face.Density,
                        flags = face.LoaderFlags,
                    }
                )
            );
        substituted = false;
        string result = Render(face.Hash, c);
        using var primary = JsonDocument.Parse(result);
        if (!primary.RootElement.TryGetProperty("missing", out _))
            return result;
        if (Instance.fallbackSources.TryGetValue((face.Hash, c), out string? fallback))
        {
            if (fallback != null)
            {
                result = Render(fallback, c);
                using var other = JsonDocument.Parse(result);
                if (!other.RootElement.TryGetProperty("missing", out _))
                    return result;
            }
        }
        else
            WantFallback(face.Hash, c);
        // The face's .notdef glyph is temporary while delivery is pending, and
        // permanent only when the host confirms no installed face has coverage.
        substituted = true;
        return Render(face.Hash, 0);
    }
}
