using System;
using System.Linq;

namespace SlopWorld.Tests
{
    static class InlineTextLayoutTests
    {
        static readonly TextSpriteCatalog Catalog = new TextSpriteCatalog(new[]
            { "\U0001f600", "\U0001f469", "\U0001f469\u200d\U0001f4bb", "\u2764\ufe0f" });
        static float Measure(string text) => text.Length * 2f;

        public static void LongestSequenceAndBmpKeys()
        {
            AssertEx.True(Catalog.Match("x👩‍💻y", 1, out int length, out int slot), "sequence found");
            AssertEx.Equal(5, length, "complete UTF-16 sequence");
            AssertEx.Equal(2, slot, "longest key takes precedence over woman glyph");
            AssertEx.True(Catalog.Match("❤️", 0, out length, out slot), "BMP sequence found");
            AssertEx.Equal(3, slot, "catalog is not restricted to a Unicode plane");
            AssertEx.False(Catalog.Match("😀", 1, out length, out slot), "low surrogate is not a glyph");
            AssertEx.False(Catalog.Match(null, 0, out length, out slot), "null is safe");
        }

        public static void ProportionalPositionsAndMeasurement()
        {
            var layout = InlineTextLayout.Proportional("ab😀cd", Catalog, 10f, Measure);
            AssertEx.Equal(18f, layout.Width, "text and sprite share measured advance");
            AssertEx.Sequence(new[] { 0f, 4f, 14f }, layout.Spans.Select(s => s.X), "positions");
            AssertEx.Sequence(new[] { -1, 0, -1 }, layout.Spans.Select(s => s.Sprite), "font spans exclude sprite text");
            AssertEx.Equal("abcd", string.Concat(layout.Spans.Where(s => s.Sprite < 0).Select(s => s.Text)),
                "covered emoji never reaches font preparation");
        }

        public static void TruncationKeepsWholeSpritesAndTextElements()
        {
            var layout = InlineTextLayout.Proportional("a👩‍💻bbb", Catalog, 10f, Measure, 14f);
            AssertEx.Equal(14f, layout.Width, "fits exactly with ellipsis");
            AssertEx.Sequence(new[] { "a", "👩‍💻", "…" }, layout.Spans.Select(s => s.Text), "sequence stays whole");
            var narrow = InlineTextLayout.Proportional("a👩‍💻bbb", Catalog, 10f, Measure, 13f);
            AssertEx.Equal("a…", string.Concat(narrow.Spans.Select(s => s.Text)), "no partial emoji");
            var combining = InlineTextLayout.Proportional("e\u0301xx", Catalog, 10f, Measure, 5f);
            AssertEx.Equal("…", string.Concat(combining.Spans.Select(s => s.Text)), "no detached combining mark");
            AssertEx.Equal(0, InlineTextLayout.Proportional("😀", Catalog, 10f, Measure, 1f).Spans.Length,
                "nothing exceeds a box narrower than ellipsis");
        }

        public static void PlainRunsKeepKerningMeasurement()
        {
            var layout = InlineTextLayout.Proportional("AV😀AV", Catalog, 10f, s => s == "AV" ? 3f : Measure(s));
            AssertEx.Equal(16f, layout.Width, "plain text is measured as runs, not summed characters");
        }

        public static void TerminalGeometryIsIndependentOfArtwork()
        {
            var wide = InlineTextLayout.Cells("a😀", 3, 7f, Catalog, c => true);
            AssertEx.Equal(21f, wide.Width, "daemon columns determine advance");
            AssertEx.Equal(14f, wide.Spans[1].Width, "final sprite occupies two cells");
            var narrow = InlineTextLayout.Cells("😀x", 2, 7f, Catalog, c => true);
            AssertEx.Equal(7f, narrow.Spans[0].Width, "same sprite may occupy one cell");
            AssertEx.Equal(7f, narrow.Spans[1].X, "next text starts at the daemon column");
            var sequence = InlineTextLayout.Cells("👩‍💻x", 4, 7f, Catalog, c => true);
            AssertEx.Equal(21f, sequence.Spans[0].Width, "sequence preserves supplied scalar cells");
            AssertEx.Equal(21f, sequence.Spans[1].X, "sequence cannot collapse terminal geometry");
        }

        public static void UnknownSupplementaryAndWideTextRemainWhole()
        {
            var layout = InlineTextLayout.Cells("\U00020000", 2, 7f, Catalog, c => true);
            AssertEx.Equal("\U00020000", layout.Spans[0].Text, "unknown glyph stays a whole scalar");
            AssertEx.Equal(-1, layout.Spans[0].Sprite, "unknown glyph uses normal font fallback");
            AssertEx.Equal(14f, layout.Spans[0].Width, "daemon width survives fallback");
        }

        public static void GeneratedCatalogSlotsAreStable()
        {
            AssertEx.True(TextSpriteData.Keys.Length <= TextSpriteData.AtlasColumns * TextSpriteData.AtlasRows,
                "all generated slots fit the atlas grid");
            for (int i = 0; i < TextSpriteData.Keys.Length; i++)
            {
                string key = TextSpriteData.Keys[i];
                AssertEx.True(TextSpriteCatalog.Shared.Match(key, 0, out int length, out int slot), "key found");
                AssertEx.Equal(i, slot, "generated slot order preserved");
                AssertEx.Equal(key.Length, length, "complete key matched");
            }
        }
    }
}
