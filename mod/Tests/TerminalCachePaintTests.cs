using System;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class TerminalCachePaintTests
    {
        [Test]
        public void RepeatedHistoryRepaintsKeepCompleteFrameUntilBackgroundAndGlyphsAreBaked()
        {
            var previous = RenderTexture.active;
            var outer = new RenderTexture();
            var cache = new RenderTexture { Pixels = new[] { "old frame" } };
            var bounds = new Rect(20, 30, 300, 400);
            var background = new Color(0.1f, 0.2f, 0.3f, 1f);
            try
            {
                RenderTexture.active = outer;
                for (int offset = 1; offset <= 3; offset++)
                {
                    var before = cache.Pixels;
                    string glyphs = "history " + offset;
                    TerminalCachePaint.Full(
                        cache,
                        bounds,
                        background,
                        () =>
                        {
                            Assert.That(RenderTexture.active, Is.SameAs(cache));
                            Assert.That(
                                cache.Pixels,
                                Is.SameAs(before),
                                "no independently visible clear"
                            );
                            Assert.That(cache.Pending, Is.EqualTo(new[] { "background" }));
                            cache.Pending.Add(glyphs);
                        }
                    );
                    Assert.That(RenderTexture.active, Is.SameAs(outer));
                    Assert.That(cache.Pixels, Is.EqualTo(new[] { "background", glyphs }));
                    Assert.That(cache.BackgroundBounds, Is.EqualTo(bounds));
                    Assert.That(cache.Background, Is.EqualTo(background));
                    Assert.That(cache.Pending, Is.Empty);
                }
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        [Test]
        public void FailedRepaintRestoresCallerTargetAndSubsequentRepaintCanComplete()
        {
            var previous = RenderTexture.active;
            var outer = new RenderTexture();
            var cache = new RenderTexture();
            try
            {
                RenderTexture.active = outer;
                Assert.Throws<InvalidOperationException>(() =>
                    TerminalCachePaint.Full(
                        cache,
                        new Rect(0, 0, 20, 20),
                        new Color(0, 0, 0, 1),
                        () => throw new InvalidOperationException("paint failed")
                    )
                );
                Assert.That(RenderTexture.active, Is.SameAs(outer));
                TerminalCachePaint.Full(
                    cache,
                    new Rect(0, 0, 20, 20),
                    new Color(0, 0, 0, 1),
                    () => cache.Pending.Add("recovered")
                );
                Assert.That(cache.Pixels, Is.EqualTo(new[] { "background", "recovered" }));
                Assert.That(RenderTexture.active, Is.SameAs(outer));
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }
    }
}
