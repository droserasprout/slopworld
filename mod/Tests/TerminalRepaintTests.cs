using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class TerminalRepaintTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return (
                "terminal crop preserves physical edges and backend orientation",
                TerminalCrop
            );
            yield return (
                "terminal scroll copy only retains fully available rows",
                TerminalCopyPlan
            );
            yield return (
                "terminal keeps cursor-only pixels and repaints sparse damage",
                TerminalDamage
            );
            yield return ("terminal broad and missing damage repaint completely", TerminalFallback);
            yield return (
                "terminal repaints damage from frames skipped between paints",
                TerminalSkippedFrames
            );
            yield return (
                "terminal invalidation overrides unchanged content",
                TerminalInvalidation
            );
            yield return ("terminal cache key checks every pixel dependency", TerminalKeys);
            yield return (
                "terminal scroll reuse requires matching rows and pixels",
                TerminalScrollReuseOpportunity
            );
            yield return (
                "terminal cache clamps only subpixel viewport overflow",
                TerminalSampling
            );
        }

        static void TerminalCrop()
        {
            var crop = TerminalCacheRegion.Crop(120.25f, 19.75f, 960.1f, 1080.4f, 1920, 1080);
            AssertEx.Equal(120, crop.X, "left floor");
            AssertEx.Equal(19, crop.Y, "top floor");
            AssertEx.Equal(841, crop.Width, "right ceiling");
            AssertEx.Equal(1061, crop.Height, "screen clamp");
            AssertEx.Equal(19, TerminalCacheRegion.TextureY(19, 100, 1080, true), "top origin");
            AssertEx.Equal(
                961,
                TerminalCacheRegion.TextureY(19, 100, 1080, false),
                "bottom origin"
            );
            var bandRegion = TerminalCacheRegion.Crop(0, 0, 1280, 1600, 1920, 1600);
            var key = new TerminalCacheKey
            {
                CellH = 20,
                Buffer = new ScreenBuf { Runs = new List<SgrRun>[80] },
            };
            bandRegion.DamageBand(key, new[] { 40 }, 1, 0, out int bandTop, out int bandHeight);
            AssertEx.Equal(799, bandTop, "dirty strip includes upper rounding pixel");
            AssertEx.Equal(22, bandHeight, "one dirty row copies only its strip");
            bandRegion.DamageBand(key, new[] { -1, 80 }, 1, 0, out _, out bandHeight);
            AssertEx.Equal(0, bandHeight, "invalid damage does not copy a strip");
            var left = TerminalCacheRegion.Crop(0, 0, 1920, 2160, 3840, 2160);
            var right = TerminalCacheRegion.Crop(1920, 0, 3840, 2160, 3840, 2160);
            AssertEx.Equal(
                3840L * 2160 * 4,
                4L * (left.Width * left.Height + right.Width * right.Height),
                "two half-screen retained textures occupy one screen"
            );
        }

        static void TerminalCopyPlan()
        {
            var old = new TerminalCacheKey
            {
                Buffer = new ScreenBuf
                {
                    Off = 10,
                    Cols = 160,
                    Rows = 80,
                    Lines = Enumerable.Range(0, 80).Select(i => "row" + i).ToArray(),
                    LinksKnown = true,
                },
                Offset = 10,
                Session = "test",
                Width = 1280,
                Height = 1600,
                CellH = 20,
            };
            foreach (int delta in new[] { -3, -1, 1, 3 })
            {
                var next = old;
                next.Offset += delta;
                next.Buffer = new ScreenBuf
                {
                    Off = next.Offset,
                    Cols = 160,
                    Rows = 80,
                    Lines = Enumerable.Range(0, 80).Select(i => "row" + (i - delta)).ToArray(),
                    LinksKnown = true,
                };
                var region = TerminalCacheRegion.Crop(0, 0, 1280, 1600, 1920, 1600);
                AssertEx.True(
                    TerminalScrollReuse.TryPlan(old, next, 1, 0, region, out var plan),
                    "matching scroll"
                );
                AssertEx.Equal(1600 - Math.Abs(delta) * 20, plan.Height, "copied pixel height");
                AssertEx.Equal(
                    80 - Math.Abs(delta) - 2,
                    plan.EndRow - plan.FirstRow,
                    "exposed rows and two boundary rows repaint"
                );
                var clipped = TerminalCacheRegion.Crop(0, 9.25f, 1280, 1591.5f, 1920, 1600);
                AssertEx.True(
                    TerminalScrollReuse.TryPlan(old, next, 1, 0, clipped, out var clippedPlan),
                    "clipped viewport retains its interior"
                );
                for (int row = clippedPlan.FirstRow; row < clippedPlan.EndRow; row++)
                {
                    AssertEx.True(
                        row * 20 >= clipped.Y + clippedPlan.DestinationTop + 20,
                        "clipped source top is repainted"
                    );
                    AssertEx.True(
                        (row + 1) * 20
                            <= clipped.Y + clippedPlan.DestinationTop + clippedPlan.Height - 20,
                        "clipped source bottom is repainted"
                    );
                }
                next.Buffer.Cols++;
                AssertEx.False(
                    TerminalScrollReuse.TryPlan(old, next, 1, 0, region, out _),
                    "column resize falls back"
                );
                next.Buffer.Cols--;

                AssertEx.Equal(Math.Max(0, delta * 20), plan.DestinationTop, "destination top");
                AssertEx.Equal(Math.Max(0, -delta * 20), plan.SourceTop, "source top");
                for (int row = plan.FirstRow; row < plan.EndRow; row++)
                {
                    AssertEx.Equal(
                        old.Buffer.Lines[row - delta],
                        next.Buffer.Lines[row],
                        "reused row text"
                    );
                    AssertEx.True(row * 20 >= plan.DestinationTop + 20, "top boundary repainted");
                    AssertEx.True(
                        (row + 1) * 20 <= plan.DestinationTop + plan.Height - 20,
                        "bottom boundary repainted"
                    );
                }
                AssertEx.False(
                    TerminalScrollReuse.TryPlan(old, next, 1.013f, 0, region, out _),
                    "fractional pixels fall back"
                );
                next.Buffer.HasLinks = true;
                AssertEx.False(
                    TerminalScrollReuse.TryPlan(old, next, 1, 0, region, out _),
                    "wrapped links fall back"
                );
                next.Buffer.HasLinks = false;
                next.Buffer.Lines[40] = "edited";
                AssertEx.False(
                    TerminalScrollReuse.TryPlan(old, next, 1, 0, region, out _),
                    "edited overlap falls back"
                );
            }
        }

        static ScreenBuf Screen(int[] changed) =>
            new ScreenBuf
            {
                ContentRevision = 2,
                Lines = new[] { "a", "b", "c", "d" },
                ChangedRows = changed,
            };

        static void TerminalDamage()
        {
            var screen = Screen(new[] { 1 });
            AssertEx.Equal(
                TerminalRepaint.Rows,
                TerminalRepaintPolicy.Choose(false, 1, screen),
                "single-row edit is selective"
            );
            screen.Cx = 3;
            screen.Seq++;
            AssertEx.Equal(
                TerminalRepaint.None,
                TerminalRepaintPolicy.Choose(false, 2, screen),
                "cursor and sequence changes reuse pixels"
            );
        }

        static void TerminalSkippedFrames()
        {
            var screen = new ScreenBuf();
            screen.FromWire(
                ProtobufFixtures.Read<Wire.ScreenView>(
                    JVal.Parse("{\"seq\":1,\"rows\":4,\"lines\":[\"old\",\"prompt\",\"\",\"\"]}")
                )
            );
            int painted = screen.ContentRevision;
            screen.FromWire(
                ProtobufFixtures.Read<Wire.ScreenView>(
                    JVal.Parse("{\"seq\":2,\"rows\":4,\"lines\":[\"new\",\"prompt\",\"\",\"\"]}")
                )
            );
            screen.FromWire(
                ProtobufFixtures.Read<Wire.ScreenView>(
                    JVal.Parse("{\"seq\":3,\"rows\":4,\"lines\":[\"new\",\"pasted\",\"\",\"\"]}")
                )
            );

            AssertEx.Equal(
                TerminalRepaint.Full,
                TerminalRepaintPolicy.Choose(false, painted, screen, out var reason),
                "latest row damage omits the unpainted change to row zero"
            );
            AssertEx.Equal(
                TerminalRepaintReason.SkippedRevisions,
                reason,
                "skipped revision has a distinct full-paint reason"
            );
            AssertEx.Equal(
                TerminalRepaint.Rows,
                TerminalRepaintPolicy.Choose(false, screen.ContentRevision - 1, screen),
                "damage is sufficient when the immediately preceding revision was painted"
            );
        }

        static void TerminalFallback()
        {
            foreach (var changed in new[] { new[] { 0, 1 }, Array.Empty<int>(), null })
            {
                AssertEx.Equal(
                    TerminalRepaint.Full,
                    TerminalRepaintPolicy.Choose(false, 1, Screen(changed), out var reason),
                    "half the rows or unknown damage repaints the whole pane"
                );
                AssertEx.Equal(
                    changed != null && changed.Length > 0
                        ? TerminalRepaintReason.BroadRows
                        : TerminalRepaintReason.MissingDamage,
                    reason,
                    "full-paint counter identifies broad rows or missing damage"
                );
            }
        }

        static void TerminalInvalidation()
        {
            AssertEx.Equal(
                TerminalRepaint.Full,
                TerminalRepaintPolicy.Choose(true, 2, Screen(Array.Empty<int>())),
                "texture, session, geometry, theme or font invalidation forces repaint"
            );
        }

        static void TerminalKeys()
        {
            var key = new TerminalCacheKey { Buffer = Screen(new[] { 1 }), Session = "a" };
            AssertEx.True(key.Matches(key), "unchanged key");
            key.Buffer.Cx++;
            key.Buffer.Seq++;
            AssertEx.True(key.Matches(key), "cursor and sequence do not invalidate pixels");
            var changes = new Func<TerminalCacheKey, TerminalCacheKey>[]
            {
                k =>
                {
                    k.Buffer = Screen(new[] { 1 });
                    return k;
                },
                k =>
                {
                    k.Session = "b";
                    return k;
                },
                k =>
                {
                    k.Offset++;
                    return k;
                },
                k =>
                {
                    k.AltScreen = !k.AltScreen;
                    return k;
                },
                k =>
                {
                    k.Theme++;
                    return k;
                },
                k =>
                {
                    k.Font++;
                    return k;
                },
                k =>
                {
                    k.X++;
                    return k;
                },
                k =>
                {
                    k.Y++;
                    return k;
                },
                k =>
                {
                    k.Width++;
                    return k;
                },
                k =>
                {
                    k.Height++;
                    return k;
                },
                k =>
                {
                    k.CellW++;
                    return k;
                },
                k =>
                {
                    k.CellH++;
                    return k;
                },
                k =>
                {
                    k.Lead++;
                    return k;
                },
            };
            foreach (var change in changes)
                AssertEx.False(key.Matches(change(key)), "pixel dependency invalidates cache");
        }

        static void TerminalScrollReuseOpportunity()
        {
            TerminalCacheKey Key(int offset, params string[] lines) =>
                new TerminalCacheKey
                {
                    Buffer = new ScreenBuf
                    {
                        Off = offset,
                        Lines = lines,
                        LinksKnown = true,
                    },
                    Session = "reader",
                    Offset = offset,
                    CellH = 19f,
                    Width = 100f,
                };

            var before = Key(10, "a", "b", "c", "d");
            var up = Key(11, "new", "a", "b", "c");
            var down = Key(9, "b", "c", "d", "new");
            AssertEx.Equal(
                3,
                TerminalScrollReuse.MatchingRows(before, up),
                "higher anchor preserves three rows at shifted indexes"
            );
            AssertEx.Equal(
                3,
                TerminalScrollReuse.MatchingRows(before, down),
                "lower anchor preserves three rows at shifted indexes"
            );
            AssertEx.Equal(
                0,
                TerminalScrollReuse.MatchingRows(before, Key(11, "new", "a", "changed", "c")),
                "one changed overlapping row disqualifies a whole-surface copy"
            );
            up.Buffer.HasLinks = true;
            AssertEx.Equal(
                0,
                TerminalScrollReuse.MatchingRows(before, up),
                "link decoration may differ across row boundaries"
            );
            up.Buffer.HasLinks = false;
            up.Theme++;
            AssertEx.Equal(
                0,
                TerminalScrollReuse.MatchingRows(before, up),
                "theme change invalidates old pixels"
            );
            AssertEx.False(
                TerminalScrollReuse.PixelAligned(10, 11, 19f, 1.75f),
                "one row is 33.25 physical pixels"
            );
            AssertEx.True(
                TerminalScrollReuse.PixelAligned(10, 14, 19f, 1.75f),
                "four rows move an integer number of physical pixels"
            );
        }

        static void TerminalSampling()
        {
            float x0 = 0f,
                y0 = 0f,
                x1 = 1429f * 2.15f,
                y1 = 774f * 2.15f;
            AssertEx.True(
                TerminalCacheSampling.TryClamp(3072, 1664, ref x0, ref y0, ref x1, ref y1),
                "rounded UI viewport remains cacheable"
            );
            AssertEx.Equal(3072f, x1, "right edge stays inside the texture");
            AssertEx.Equal(1664f, y1, "bottom edge stays inside the texture");

            x0 = 0f;
            y0 = 0f;
            x1 = 3074f;
            y1 = 1664f;
            AssertEx.False(
                TerminalCacheSampling.TryClamp(3072, 1664, ref x0, ref y0, ref x1, ref y1),
                "larger overflow still needs a fallback"
            );
            x0 = -0.25f;
            y0 = -0.5f;
            x1 = 100f;
            y1 = 100f;
            AssertEx.True(
                TerminalCacheSampling.TryClamp(3072, 1664, ref x0, ref y0, ref x1, ref y1),
                "small negative edges clamp too"
            );
            AssertEx.Equal(0f, x0, "left edge stays inside the texture");
            AssertEx.Equal(0f, y0, "top edge stays inside the texture");
        }
    }
}
