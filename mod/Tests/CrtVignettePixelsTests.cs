using NUnit.Framework;

namespace SlopWorld.Tests
{
    public class CrtVignettePixelsTests
    {
        [TestCase(1f)]
        [TestCase(1.25f)]
        [TestCase(1.5f)]
        [TestCase(2f)]
        public void CombinedOverlayPreservesTwoPassBlendWithinOneDisplayLevel(float scale)
        {
            const int width = 127,
                height = 73;
            foreach (
                string mode in new[]
                {
                    VisualEffectsMode.Mild,
                    VisualEffectsMode.Moderate,
                    VisualEffectsMode.Massive,
                }
            )
            {
                float corner = VisualEffectsMode.CornerOpacity(mode);
                float scan = VisualEffectsMode.ScanlineOpacity(mode);
                var original = CrtVignetteReference.Build(width, height, corner);
                var combined = CrtVignettePixels.Build(width, height, corner, scan, scale);
                for (int y = 0; y < height; y++)
                {
                    // Independent overlap integral for the one dark logical row per period.
                    float start = (height - 1 - y) / scale;
                    float end = (height - y) / scale;
                    float coverage = 0f;
                    for (int row = 0; row <= height; row += 3)
                        coverage += System.Math.Max(
                            0f,
                            System.Math.Min(end, row + 1f) - System.Math.Max(start, row)
                        );
                    float scanAlpha =
                        (float)
                            System.Math.Round(scan * System.Math.Min(1f, coverage * scale) * 255f)
                        / 255f;
                    for (int x = 0; x < width; x++)
                    {
                        var v = original[y * width + x];
                        var c = combined[y * width + x];
                        foreach (int background in new[] { 0, 64, 128, 255 })
                        {
                            float expected =
                                v.r * (v.a / 255f)
                                + background * (1f - scanAlpha) * (1f - v.a / 255f);
                            float actual = c.r * (c.a / 255f) + background * (1f - c.a / 255f);
                            Assert.That(
                                System.Math.Abs(actual - expected),
                                Is.LessThanOrEqualTo(1f)
                            );
                        }
                    }
                }
            }
        }

        [TestCase(1, 1)]
        [TestCase(3, 5)]
        [TestCase(127, 73)]
        [TestCase(1920, 1080)]
        [TestCase(2560, 1440)]
        [TestCase(3840, 2160)]
        public void PrecomputedCoordinatesPreserveEveryPixel(int width, int height)
        {
            foreach (
                string mode in new[]
                {
                    VisualEffectsMode.Mild,
                    VisualEffectsMode.Moderate,
                    VisualEffectsMode.Massive,
                }
            )
            {
                float opacity = VisualEffectsMode.CornerOpacity(mode);
                var expected = CrtVignetteReference.Build(width, height, opacity);
                var actual = CrtVignettePixels.Build(width, height, opacity);
                Assert.That(actual.Length, Is.EqualTo(expected.Length));
                for (int i = 0; i < expected.Length; i++)
                {
                    var a = actual[i];
                    var e = expected[i];
                    if (a.r != e.r || a.g != e.g || a.b != e.b || a.a != e.a)
                        Assert.Fail($"{mode} pixel {i % width},{i / width} changed");
                }
            }
        }
    }
}
