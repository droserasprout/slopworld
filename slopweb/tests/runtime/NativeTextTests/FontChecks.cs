// CPU-only bank lifetime tests: no native rasterizer, renderer, game or browser.
using System.Reflection;
using System.Text.Json;
using BrowserAdapters;
using UnityEngine;

static class FontChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run()
    {
        var bank = BrowserFontBank.Instance;
        var type = typeof(BrowserFontBank);
        var bytesField = type.GetField("atlasBytes", Private)!;
        var rootField = type.GetField("mountRoot", BindingFlags.Static | BindingFlags.NonPublic)!;
        string root = Path.Combine(Path.GetTempPath(), "slopweb-font-test-" + Guid.NewGuid());
        object previousRoot = rootField.GetValue(null)!;
        long previousBytes = (long)bytesField.GetValue(bank)!;
        rootField.SetValue(null, root);
        byte[] font = [1, 2, 3];
        string Bake(int size, int scalar = -1, float advance = 1, float density = 1) =>
            JsonSerializer.Serialize(
                new
                {
                    atlas = new
                    {
                        width = 1,
                        height = 1,
                        bytesPerPixel = 1,
                        pixels = "/w==",
                    },
                    size,
                    density,
                    style = 0,
                    ascent = 1,
                    descent = 0,
                    lineMetrics = new
                    {
                        Ascent = 1,
                        FirstLine = 2,
                        Pitch = 2,
                        Spacing = 2,
                    },
                    glyphs = scalar < 0
                        ? Array.Empty<object>()
                        : new object[]
                        {
                            new
                            {
                                scalar,
                                fallback = false,
                                visible = true,
                                advance,
                                x0 = 0,
                                y0 = 0,
                                x1 = 1,
                                y1 = 1,
                                u0 = 0,
                                v0 = 0,
                                u1 = 1,
                                v1 = 1,
                            },
                        },
                }
            );
        try
        {
            int old = BrowserFontBank.Install(font, Bake(14), -1);
            int texture = BrowserFontBank.Instance.GetAtlasTexture(old).GetInstanceID();
            int revision = bank.AtlasVersion;
            if (
                BrowserFontBank.Install(font, Bake(14), -1) != old
                || bank.AtlasVersion != revision
                || BrowserFontBank.Instance.GetAtlasTexture(old).GetInstanceID() != texture
            )
                throw new Exception("Repeated installation replaced a live atlas");
            // A previous miss must be invalidated by temporary registration.
            if (BrowserFontBank.TryFace(old, 18, out _))
                throw new Exception("Unexpected size");
            int resolution = bank.ResolutionVersion;
            if (
                BrowserFontBank.RegisterAvailable(font, 18) != old
                || !BrowserFontBank.TryFace(old, 18, out var pending)
                || pending.Size != 14
            )
                throw new Exception("Pending size lost its usable previous face");
            if (bank.ResolutionVersion == resolution || bank.AtlasVersion != revision)
                throw new Exception(
                    "Temporary registration must invalidate resolution without changing atlas"
                );
            int exact = BrowserFontBank.Install(font, Bake(18), -1);
            if (
                exact == old
                || BrowserFontBank.RegisterAvailable(font, 18) != exact
                || !BrowserFontBank.TryFace(old, 18, out var replaced)
                || replaced.Size != 18
            )
                throw new Exception("Exact bake did not replace temporary size fallback");
            bytesField.SetValue(bank, 384L * 1024 * 1024);
            try
            {
                BrowserFontBank.Install(font, Bake(20), -1);
                throw new Exception("Budget admitted another atlas");
            }
            catch (InvalidOperationException) { }
            if (
                !BrowserFontBank.TryFace(old, 14, out var retained)
                || retained.Texture.GetInstanceID() != texture
            )
                throw new Exception("Budget failure destroyed the old face");
            bytesField.SetValue(bank, previousBytes + 8);
            for (int size = 100; size < 1300; size++)
                BrowserFontBank.RegisterAvailable(font, size);
            if (
                (
                    (System.Collections.ICollection)type.GetField("faces", Private)!.GetValue(bank)!
                ).Count != 2
            )
                throw new Exception("Pending registrations allocated faces");
            var pendingSet = type.GetField("pendingSizes", Private)!.GetValue(bank)!;
            if ((int)pendingSet.GetType().GetProperty("Count")!.GetValue(pendingSet)! > 1024)
                throw new Exception("Pending registrations are unbounded");
            int beforeReplacement = bank.AtlasVersion;
            BrowserFontBank.Install(font, Bake(14, '中', 3), -1);
            if (
                !BrowserFontBank.TryFace(old, 14, out var expanded)
                || !expanded.Glyphs.TryGetValue('中', out var added)
                || added.advanceX != 3
                || expanded.Texture.GetInstanceID() == texture
                || bank.AtlasVersion <= beforeReplacement
                || (long)bytesField.GetValue(bank)! != previousBytes + 8
            )
                throw new Exception(
                    "Atlas replacement lost glyphs, stable index, revision or memory accounting"
                );
            BrowserFontBank.Install(font, Bake(14), -1);
            if (!bank.TryGetGlyph(old, '中', out var preserved) || preserved.advanceX != 3)
                throw new Exception("Metrics-only selection erased learned coverage");
            var gui = BrowserGui.Install();
            gui.DrawText(
                new Rect(0, 0, 100, 100),
                "中",
                old,
                14,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                false,
                Color.white,
                default,
                false
            );
            gui.Drain();
            BrowserFontBank.Install(font, Bake(14, '中', 5), -1);
            if (!bank.TryGetGlyph(old, '中', out var changed) || changed.advanceX != 5)
                throw new Exception("Replaced atlas retained stale metrics");
            BrowserGui.FontsChanged();
            if (
                !ReferenceEquals(UnityStubs.Gui.GuiBackend.Current, gui)
                || !gui.Drain().Contains("releaseImage")
            )
                throw new Exception("Font refresh orphaned the previous browser atlas");
            BrowserFontBank.ConfigureDensity(1.25f);
            BrowserFontBank.Install(font, Bake(14, '中', 5, 1.25f), -1);
            if (!BrowserFontBank.TryFace(old, 14, out var scaled) || scaled.Density != 1.25f)
                throw new Exception("Exact density did not replace resampled fallback");
            BrowserFontBank.ConfigureDensity(1);
        }
        finally
        {
            BrowserFontBank.ConfigureDensity(1);
            bytesField.SetValue(bank, previousBytes);
            rootField.SetValue(null, previousRoot);
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
        Console.WriteLine(
            "Font bank deduplication, pending/exact handoff, budget rollback and bounded registrations passed."
        );
    }
}
