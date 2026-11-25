// Atlas/lifetime failure tests use controlled raster outputs, independently of the
// real WASM/native fidelity comparison in local-fonts.test.mjs.
using System.Reflection;
using System.Text.Json;
using BrowserAdapters;
using UnityEngine;

static class LocalFontChecks
{
    internal static void Run(byte[] font)
    {
        int calls = 0,
            loads = 0,
            closes = 0;
        bool fail = true;
        bool pendingGlyph = true;
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
            if ((scalar == 'Ж' || scalar == 0x1F603) && pendingGlyph)
                return "{\"pending\":true}";
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
        int index = BrowserFontBank.Install(font, "{\"size\":15,\"loaderFlags\":513}", -1);
        BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, "A\uffff!😀");
        int initial = calls;
        BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, "A\uffff!😀");
        if (calls != initial || initial != 5)
            throw new Exception("Coverage/failure/surrogate cache boundary");
        BrowserFontBank.TryFace(index, 15, out var face);
        if (
            !face.Glyphs.ContainsKey(0x1F600)
            || !face.Glyphs.TryGetValue('A', out var glyph)
            || glyph.advanceX != 7.25f
            || glyph.x0 != -1
        )
            throw new Exception("Fractional metrics or bearings changed");
        for (int i = 0; i < 1000; i++)
            BrowserFontBank.Instance.TryGetGlyphAtSize(index, 'A', 15, out _, out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
            if (!BrowserFontBank.Instance.TryGetGlyphAtSize(index, 'A', 15, out _, out _))
                throw new Exception("Cached glyph missing");
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (bytes != 0)
            throw new Exception($"Cached glyph queries allocated {bytes} bytes");
        Console.WriteLine($"Allocated for 10000 cached glyph queries: {bytes}");
        int revision = BrowserFontBank.Instance.AtlasVersion;
        if (
            BrowserFontBank.Install(font, "{\"size\":15,\"loaderFlags\":513}", -1) != index
            || BrowserFontBank.Instance.AtlasVersion != revision
            || !BrowserFontBank.TryFace(index, 15, out var reinstalled)
            || !ReferenceEquals(reinstalled, face)
            || !reinstalled.Glyphs.TryGetValue('A', out var kept)
            || kept.advanceX != glyph.advanceX
            || loads != 1
        )
            throw new Exception("Repeated outline installation replaced a populated atlas");
        fail = false;
        BrowserFontBank.RetryRasterization();
        BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, "!");
        if (!face.Glyphs.ContainsKey('!'))
            throw new Exception("Failed raster did not recover");
        BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, "Ж");
        if (face.Glyphs.ContainsKey('Ж'))
            throw new Exception("Pending glyph was committed as a permanent result");
        var pendingGui = BrowserGui.Install();
        const string pendingText = "A😃\nA";
        var pendingCaret = pendingGui.TextCursorPos(
            index,
            15,
            FontStyle.Normal,
            pendingText,
            100,
            4,
            out _
        );
        if (pendingCaret.y <= 0 || face.Glyphs.ContainsKey(0x1F603))
            throw new Exception("Pending supplementary glyph lost its line or stopped retrying");
        pendingGlyph = false;
        BrowserFontBank.GlyphsReady();
        pendingGui.TextCursorPos(index, 15, FontStyle.Normal, pendingText, 100, 4, out _);
        if (!face.Glyphs.ContainsKey(0x1F603))
            throw new Exception("Completed supplementary glyph did not replace its placeholder");
        BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, "Ж");
        if (!face.Glyphs.ContainsKey('Ж'))
            throw new Exception("Completed worker glyph did not recover");
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
        const string multiline = "A😀\nBC";
        appendGui.DrawText(
            new Rect(0, 0, 100, 100),
            multiline,
            index,
            15,
            FontStyle.Normal,
            TextAnchor.UpperLeft,
            true,
            Color.white
        );
        if (appendGui.Drain().Contains("\"kind\":\"text\""))
            throw new Exception("A supplementary glyph bypassed multiline glyph rendering");
        var nextLine = appendGui.TextCursorPos(
            index,
            15,
            FontStyle.Normal,
            multiline,
            100,
            4,
            out _
        );
        if (
            nextLine.x != 0
            || nextLine.y <= 0
            || appendGui.TextIndexAt(index, 15, FontStyle.Normal, multiline, 100, nextLine) != 4
            || appendGui.TextIndexAt(index, 15, FontStyle.Normal, "A😀", 100, new Vector2(15, 0))
                != 3
        )
            throw new Exception("Supplementary glyphs lost UTF-16 caret indices or line geometry");
        var selection = new List<Rect>();
        foreach (
            var sample in new[]
            {
                (Text: "ABC", Start: 1, End: 3),
                (Text: multiline, Start: 4, End: 6),
            }
        )
        {
            var start = appendGui.TextCursorPos(
                index,
                15,
                FontStyle.Normal,
                sample.Text,
                100,
                sample.Start,
                out var height
            );
            var end = appendGui.TextCursorPos(
                index,
                15,
                FontStyle.Normal,
                sample.Text,
                100,
                sample.End,
                out _
            );
            appendGui.TextSelectionRects(
                index,
                15,
                FontStyle.Normal,
                sample.Text,
                100,
                sample.Start,
                sample.End,
                selection
            );
            if (
                selection.Count != 1
                || selection[0] != new Rect(start.x, start.y, end.x - start.x, height)
            )
                throw new Exception("Selection bounds diverged from caret positions");
        }
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
        BrowserGui.FontsChanged();
        if (
            !ReferenceEquals(UnityStubs.Gui.GuiBackend.Current, appendGui)
            || !appendGui.Drain().Contains("releaseImage")
        )
            throw new Exception("Font refresh lost the previous browser atlas release");
        var grown = face.Texture;
        var bytesField = typeof(BrowserFontBank).GetField(
            "atlasBytes",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        long retainedBytes = (long)bytesField.GetValue(BrowserFontBank.Instance)!;
        bytesField.SetValue(BrowserFontBank.Instance, 384L * 1024 * 1024);
        try
        {
            BrowserFontBank.RegisterAvailable(font, 20);
            throw new Exception("Font budget admitted a new size");
        }
        catch (InvalidOperationException) { }
        if (BrowserFontBank.RegisterAvailable(font, 15) != index)
            throw new Exception("Font budget failure lost the installed face");
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
        CheckFallbackDelivery(font);
        CheckNativeLineMetrics();
        Console.WriteLine(
            "Local glyph cache, atlas growth, style/density, fallback suppression, failure recovery and repeated disposal passed."
        );
    }

    static void CheckFallbackDelivery(byte[] primary)
    {
        byte[] fallback = (byte[])primary.Clone();
        fallback[79] = 1;
        string fallbackHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(fallback)
        );
        bool rejectMissing = false;
        BrowserFontBank.Rasterize = request =>
        {
            using var doc = JsonDocument.Parse(request);
            var v = doc.RootElement;
            if (!v.TryGetProperty("scalar", out var scalar))
                return v.TryGetProperty("size", out _) ? "{\"ascent\":13,\"descent\":-4}" : "{}";
            int c = scalar.GetInt32();
            if (c == 0 && rejectMissing)
                throw new InvalidOperationException("Missing marker raster failure");
            bool other = v.GetProperty("hash").GetString() == fallbackHash;
            if (c is 0x2602 or 0x1F600 && !other)
                return "{\"missing\":true}";
            return JsonSerializer.Serialize(
                new
                {
                    advance = other ? 11 : 7,
                    x = 0,
                    y = -9,
                    ascent = other ? 20 : 13,
                    descent = -4,
                    width = 1,
                    height = 1,
                    pixels = "/w==",
                }
            );
        };
        int index = BrowserFontBank.Install(
            primary,
            "{\"size\":15,\"loaderFlags\":513,\"family\":\"Primary\"}",
            -1
        );
        BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, "A😀");
        BrowserFontBank.TryFace(index, 15, out var face);
        if (face.Glyphs[0x1F600].advanceX != 7)
            throw new Exception("Pending fallback must use primary missing glyph");
        using var pending = JsonDocument.Parse(BrowserFontBank.TakeFallbacks());
        long id = pending.RootElement.GetProperty("id").GetInt64();
        if (BrowserFontBank.TakeFallbacks() != "")
            throw new Exception("Duplicate fallback request");
        string reply = JsonSerializer.Serialize(
            new { id, assignments = new Dictionary<string, string?> { ["128512"] = fallbackHash } }
        );
        try
        {
            BrowserFontBank.CompleteFallbacks(reply);
            throw new Exception("Uninstalled fallback accepted");
        }
        catch (InvalidDataException) { }
        BrowserFontBank.Install(
            fallback,
            "{\"size\":15,\"loaderFlags\":513,\"family\":\"Fallback\"}",
            -1
        );
        BrowserFontBank.CompleteFallbacks(reply);
        BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, "A😀");
        if (
            face.Glyphs['A'].advanceX != 7
            || face.Glyphs[0x1F600].advanceX != 11
            || face.Ascent != 13
            || face.Glyphs[0x1F600].y0 != 4
        )
            throw new Exception("Fallback changed primary metrics or lost baseline alignment");
        BrowserFontBank.CompleteFallbacks(reply);
        if (BrowserFontBank.TakeFallbacks() != "")
            throw new Exception("Resolved coverage requested again");
        var gui = BrowserGui.Install();
        gui.DrawText(
            new Rect(0, 0, 100, 30),
            "A😀",
            index,
            15,
            FontStyle.Normal,
            TextAnchor.UpperLeft,
            false,
            Color.white,
            default,
            false
        );
        if (gui.Drain().Contains("\"kind\":\"text\""))
            throw new Exception("Mixed coverage escaped to CSS text");
        rejectMissing = true;
        BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, "☂");
        using var recovery = JsonDocument.Parse(BrowserFontBank.TakeFallbacks());
        BrowserFontBank.CompleteFallbacks(
            JsonSerializer.Serialize(
                new
                {
                    id = recovery.RootElement.GetProperty("id").GetInt64(),
                    assignments = new Dictionary<string, string?> { ["9730"] = fallbackHash },
                }
            )
        );
        BrowserFontBank.ObserveText(index, 15, FontStyle.Normal, "☂");
        if (face.Glyphs['☂'].advanceX != 11)
            throw new Exception("Failed missing marker blocked delivered fallback");
        BrowserFontBank.DisposeFonts();
        BrowserFontBank.CompleteFallbacks(reply);
        if (BrowserFontBank.Paths().Length != 0)
            throw new Exception("Stale reply revived disposed fonts");
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
