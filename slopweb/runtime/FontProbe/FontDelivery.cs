// Host fallback policy resolves installed files; only extracted font bytes and
// family identities cross the boundary. No glyph images or filesystem paths do.
using System.Globalization;
using System.Reflection;
using System.Text.Json;

internal static class FontDelivery
{
    internal static void Deliver(string modPath, string request, string output, string game)
    {
        using var json = JsonDocument.Parse(request);
        string family = json.RootElement.GetProperty("family").GetString()!;
        var pending = json
            .RootElement.GetProperty("scalars")
            .EnumerateArray()
            .Select(v => v.GetInt32())
            .ToHashSet();
        if (
            pending.Count > 256
            || pending.Any(c => c < 0 || c > 0x10ffff || c is >= 0xd800 and <= 0xdfff)
        )
            throw new ArgumentException("Invalid fallback coverage");
        var mod = Assembly.LoadFrom(Path.GetFullPath(modPath));
        var native = mod.GetType("SlopWorld.NativeFonts", true)!;
        var catalog = native
            .GetMethod("Scan", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, null)!;
        var paths =
            (Dictionary<string, (string Path, int Face)>)
                catalog
                    .GetType()
                    .GetField("Paths", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(catalog)!;
        var families = (string[])
            mod.GetType("SlopWorld.FontconfigFonts", true)!
                .GetMethod("Families")!
                .Invoke(null, [family])!;
        var extract = mod.GetType("SlopWorld.OpenTypeCollection", true)!
            .GetMethod(
                "Extract",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            )!;
        var host = Assembly
            .LoadFrom(Path.Combine(game, "RimWorld.dll"))
            .GetType("RimWorldWaylith.Imgui.ImGuiHost", true)!;
        uint flags = (uint)
            host.GetMethod("LoaderFlagsFor", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [0])!;
        Directory.CreateDirectory(output);
        var manifest = new List<object>();
        var assignments = pending.ToDictionary(
            c => c.ToString(CultureInfo.InvariantCulture),
            _ => (string?)null
        );
        // Fontconfig order is authoritative. The catalog tail allows installed
        // families omitted by Fontconfig to supply otherwise uncovered characters.
        foreach (
            string name in families
                .Concat(paths.Keys.Order(StringComparer.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
        )
        {
            if (pending.Count == 0)
                break;
            if (!paths.TryGetValue(name, out var source))
                continue;
            byte[] bytes;
            try
            {
                bytes = (byte[])
                    extract.Invoke(null, [File.ReadAllBytes(source.Path), source.Face])!;
            }
            catch (Exception e) when (e is IOException or TargetInvocationException)
            {
                continue;
            }
            int[] covered = pending.Where(c => FontCoverage.Contains(bytes, c)).ToArray();
            if (covered.Length == 0)
                continue;
            string file = "face" + manifest.Count.ToString(CultureInfo.InvariantCulture) + ".ttf",
                descriptor =
                    "face" + manifest.Count.ToString(CultureInfo.InvariantCulture) + ".json";
            File.WriteAllBytes(Path.Combine(output, file), bytes);
            File.WriteAllText(
                Path.Combine(output, descriptor),
                JsonSerializer.Serialize(
                    new
                    {
                        size = 14,
                        loaderFlags = flags,
                        family = name,
                    }
                )
            );
            manifest.Add(new { font = file, bake = descriptor });
            foreach (int scalar in covered)
            {
                assignments[scalar.ToString(CultureInfo.InvariantCulture)] = file;
                pending.Remove(scalar);
            }
        }
        File.WriteAllText(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(manifest)
        );
        File.WriteAllText(
            Path.Combine(output, "assignments.json"),
            JsonSerializer.Serialize(assignments)
        );
        File.WriteAllText(Path.Combine(output, "chains.json"), "{}");
    }
}
