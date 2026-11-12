// Atlas/lifetime failure tests use controlled raster outputs, independently of the
// real WASM/native fidelity comparison in local-fonts.test.mjs.
using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;
using BrowserAdapters;
using UnityEngine;

static class LocalFontChecks
{
    internal static void Run(string game)
    {
        var rootField = typeof(BrowserFontBank).GetField(
            "mountRoot",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        var previous = rootField.GetValue(null);
        string root = Path.Combine(Path.GetTempPath(), "local-font-check-" + Guid.NewGuid());
        rootField.SetValue(null, root);
        // Only head/hhea are needed by the managed metrics reader. Actual font
        // parsing/rasterization is covered separately by the WASM fidelity test.
        byte[] font = new byte[80];
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(12), 0x68656164);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(20), 44);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(28), 0x68686561);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(36), 64);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(62), 1000);
        BinaryPrimitives.WriteInt16BigEndian(font.AsSpan(68), 900);
        BinaryPrimitives.WriteInt16BigEndian(font.AsSpan(70), -300);
        int calls = 0,
            loads = 0,
            closes = 0;
        bool fail = true;
        BrowserFontBank.Rasterize = request =>
        {
            using var document = JsonDocument.Parse(request);
            var item = document.RootElement;
            if (item.TryGetProperty("close", out _))
            {
                closes++;
                return "{}";
            }
            if (item.TryGetProperty("font", out _))
            {
                loads++;
                return "{}";
            }
            if (!item.TryGetProperty("scalar", out _))
                return "{\"ascent\":13,\"descent\":-4}";
            calls++;
            int scalar = item.GetProperty("scalar").GetInt32();
            if (scalar == 0xFFFF)
                return "{\"missing\":true}";
            if (scalar == '!' && fail)
                throw new InvalidOperationException("controlled failure");
            return JsonSerializer.Serialize(
                new
                {
                    advance = 7.25f,
                    x = -1f,
                    y = -10f,
                    ascent = 13f,
                    descent = -4f,
                    width = 40,
                    height = 40,
                    pixels = Convert.ToBase64String(Enumerable.Repeat((byte)127, 1600).ToArray()),
                }
            );
        };
        try
        {
            int index = BrowserFontBank.Install(font, "{\"size\":15,\"loaderFlags\":513}", -1);
            BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, "A\uffff!😀");
            int initial = calls;
            BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, "A\uffff!😀");
            if (calls != initial || initial != 3)
                throw new Exception("Coverage/failure/surrogate cache boundary");
            BrowserFontBank.TryFace(index, 15, out var face);
            if (
                !face.Glyphs.TryGetValue('A', out var glyph)
                || glyph.advanceX != 7.25f
                || glyph.x0 != -1
            )
                throw new Exception("Fractional metrics or bearings changed");
            fail = false;
            BrowserFontBank.RetryRasterization();
            BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, "!");
            if (!face.Glyphs.ContainsKey('!'))
                throw new Exception("Failed raster did not recover");
            var appendGui = BrowserGui.Install();
            foreach (string observed in new[] { "A", "B", "C" })
                appendGui.DrawText(
                    new Rect(0, 0, 100, 100),
                    observed,
                    index,
                    15,
                    FontStyle.Normal,
                    TextAnchor.UpperLeft,
                    false,
                    Color.white,
                    default,
                    false
                );
            using (var packet = JsonDocument.Parse(appendGui.Drain()))
                if (
                    packet
                        .RootElement.EnumerateArray()
                        .Count(c => c.GetProperty("kind").GetString() == "image") != 1
                )
                    throw new Exception(
                        "Incremental glyphs retained duplicate atlas uploads in one frame"
                    );
            var old = face.Texture;
            if (BrowserNativeLayout.Available)
            {
                appendGui.MeasureText(index, 15, FontStyle.Normal, "<b>Q</b>z", -1, true);
                BrowserFontBank.TryFace(index, 15, FontStyle.Bold, out var bold);
                if (
                    !bold.Glyphs.ContainsKey('Q')
                    || bold.Glyphs.ContainsKey('z')
                    || !face.Glyphs.ContainsKey('z')
                    || face.Glyphs.ContainsKey('<')
                    || face.Glyphs.ContainsKey('Q')
                )
                    throw new Exception("Rich layout rasterized markup or unobserved styles");
            }
            BrowserFontBank.ObserveText(
                index,
                15,
                FontStyle.Normal,
                new string(Enumerable.Range(32, 100).Select(n => (char)n).ToArray())
            );
            if (face.Texture.width <= 256 || old != null || face.Glyphs['A'].u1 >= glyph.u1)
                throw new Exception("Atlas growth did not remap UVs or retire texture");
            var grown = face.Texture;
            var bytesField = typeof(BrowserFontBank).GetField(
                "atlasBytes",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            long retainedBytes = (long)bytesField.GetValue(BrowserFontBank.Instance)!;
            bytesField.SetValue(BrowserFontBank.Instance, 384L * 1024 * 1024);
            string extra = new string(Enumerable.Range(200, 500).Select(n => (char)n).ToArray());
            BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, extra);
            if (!ReferenceEquals(grown, face.Texture) || face.Glyphs.ContainsKey(extra[^1]))
                throw new Exception("Atlas budget failure replaced a usable texture");
            bytesField.SetValue(BrowserFontBank.Instance, retainedBytes);
            BrowserFontBank.RetryRasterization();
            BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, extra);
            if (!face.Glyphs.ContainsKey(extra[^1]))
                throw new Exception("Atlas budget recovery failed");
            grown = face.Texture;
            BrowserFontBank.ConfigureDensity(1.25f);
            if (
                face.Glyphs.Count != 0
                || !ReferenceEquals(face.Texture, null)
                || typeof(BrowserFontBank.Face)
                    .GetField("Pixels", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(face) != null
            )
                throw new Exception("Stable font index retained retired atlas buffers");
            BrowserFontBank.ObserveText(index, 15, FontStyle.BoldAndItalic, "A");
            BrowserFontBank.TryFace(index, 15, FontStyle.BoldAndItalic, out var dense);
            if (grown != null || dense.Density != 1.25f || dense.Style != FontStyle.BoldAndItalic)
                throw new Exception("Density/style resource replacement failed");
            int resized = BrowserFontBank.RegisterAvailable(font, 17.5f);
            if (resized == index || loads != 1)
                throw new Exception("Font bytes were reloaded for a size change");
            var gui = BrowserGui.Install();
            gui.DrawText(
                new Rect(0, 0, 100, 100),
                "A",
                resized,
                17.5f,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                false,
                Color.white,
                default,
                false
            );
            if (gui.Drain().Contains("\"kind\":\"text\""))
                throw new Exception("Loaded glyph flashed fallback text");
            BrowserFontBank.DisposeFonts();
            BrowserFontBank.DisposeFonts();
            if (dense.Texture != null || closes != 2 || BrowserFontBank.Paths().Length != 0)
                throw new Exception("Repeated disposal leaked resources");
            CheckNativeLineMetrics();
        }
        finally
        {
            BrowserFontBank.DisposeFonts();
            BrowserFontBank.Rasterize = null;
            BrowserFontBank.ConfigureDensity(1);
            rootField.SetValue(null, previous);
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
        Console.WriteLine(
            "Local glyph cache, atlas growth, style/density, fallback suppression, failure recovery and repeated disposal passed."
        );
    }

    static void CheckNativeLineMetrics()
    {
        string oracle = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../../runtime/out/local-font-check")
        );
        if (!Directory.Exists(oracle))
            return;
        int compared = 0;
        foreach (string path in Directory.EnumerateFiles(oracle, "*.json"))
        {
            string json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            var native = document.RootElement;
            if (
                native.ValueKind != JsonValueKind.Object
                || !native.TryGetProperty("fontPath", out var source)
                || !File.Exists(source.GetString())
            )
                continue;
            // FreeType ascent/descent are independently compared by the real WASM
            // test. Here the oracle protects the managed head/hhea line geometry.
            string metrics = JsonSerializer.Serialize(
                new
                {
                    ascent = native.GetProperty("ascent").GetSingle(),
                    descent = native.GetProperty("descent").GetSingle(),
                }
            );
            BrowserFontBank.Rasterize = request =>
                request.Contains("\"size\"", StringComparison.Ordinal) ? metrics : "{}";
            BrowserFontBank.ConfigureDensity(native.GetProperty("density").GetSingle());
            float size = native.GetProperty("size").GetSingle();
            int index = BrowserFontBank.Install(File.ReadAllBytes(source.GetString()!), json, -1);
            BrowserFontBank.TryFace(index, size, out var face);
            var line = native.GetProperty("lineMetrics");
            if (
                face.LineAscent != line.GetProperty("Ascent").GetSingle()
                || face.FirstLine != line.GetProperty("FirstLine").GetSingle()
                || face.Pitch != line.GetProperty("Pitch").GetSingle()
                || face.Spacing != line.GetProperty("Spacing").GetSingle()
            )
                throw new Exception("Local line geometry differs from FontProbe: " + path);
            compared++;
        }
        BrowserFontBank.DisposeFonts();
        Console.WriteLine(
            $"{compared} installed-font line metric variants matched native FontProbe."
        );
    }
}
