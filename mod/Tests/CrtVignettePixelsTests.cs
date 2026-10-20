using NUnit.Framework;

namespace SlopWorld.Tests
{
    public class CrtVignettePixelsTests
    {
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
