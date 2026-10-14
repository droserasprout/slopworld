using System;
using System.Reflection;
using UnityEngine;
using Verse;

namespace SlopWorld.Tests
{
    static class SharedTextRendererTests
    {
        static void Reset(Texture2D atlas)
        {
            typeof(SharedTextRenderer)
                .GetField("_looked", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, false);
            typeof(SharedTextRenderer)
                .GetField("_atlas", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, null);
            ContentFinder<Texture2D>.Result = atlas;
            GUI.TextGroups.Clear();
            GUI.TextLabels.Clear();
            GUI.LabelClippings.Clear();
            GUI.LabelColors.Clear();
            GUI.FailLabel = false;
            GUI.SpriteColors.Clear();
            GUI.SpriteDraws.Clear();
            GUI.TextGroupDepth = 0;
            GUI.FailSprite = false;
        }

        public static void TerminalForegroundChangesBetweenRunsAndRestoresCallerTint()
        {
            Reset(new Texture2D());
            Color previous = GUI.color;
            var tint = new Color(0.8f, 0.6f, 0.4f, 0.5f);
            var red = new Color(1f, 0.1f, 0.2f, 0.8f);
            var blue = new Color(0.2f, 0.3f, 1f, 0.6f);
            var style = new GUIStyle();
            var plain = InlineTextLayout.Cells("a", 1, 6f, TextSpriteCatalog.Shared, c => true);
            var mixed = InlineTextLayout.Cells("a😀b", 4, 6f, TextSpriteCatalog.Shared, c => true);
            var bounds = new Rect(0, 0, 30, 14);
            try
            {
                GUI.color = tint;
                style.normal.textColor = red;
                SharedTextRenderer.DrawTerminalRun(plain, bounds, 14f, style, 6f);
                AssertEx.Equal(
                    tint * red,
                    GUI.LabelColors[0],
                    "plain glyph retains foreground and caller opacity"
                );
                AssertEx.Equal(tint, GUI.color, "plain run restores caller tint");
                style.normal.textColor = blue;
                SharedTextRenderer.DrawTerminalRun(mixed, bounds, 14f, style, 6f);
                AssertEx.Equal(
                    tint * blue,
                    GUI.LabelColors[1],
                    "mixed run uses its current foreground"
                );
                AssertEx.Equal(
                    tint * blue,
                    GUI.LabelColors[2],
                    "text after sprite retains foreground"
                );
                AssertEx.Equal(
                    new Color(1, 1, 1, tint.a),
                    GUI.SpriteColors[0],
                    "colored glyph keeps its own artwork"
                );
                AssertEx.Equal(tint, GUI.color, "mixed run restores caller tint");
                GUI.FailLabel = true;
                AssertEx.Throws<InvalidOperationException>(
                    () => SharedTextRenderer.DrawTerminalRun(plain, bounds, 14f, style, 6f),
                    "label failure"
                );
                AssertEx.Equal(tint, GUI.color, "failed run restores caller tint");
                AssertEx.Equal(
                    TextClipping.Overflow,
                    style.clipping,
                    "failed run restores style clip"
                );
            }
            finally
            {
                GUI.color = previous;
                GUI.FailLabel = false;
            }
        }

        public static void PlainTerminalRunUsesLabelClippingWithoutGroup()
        {
            Reset(null);
            var style = new GUIStyle { clipping = TextClipping.Overflow };
            var layout = InlineTextLayout.Cells("abc", 3, 6f, TextSpriteCatalog.Shared, c => true);
            var bounds = new Rect(10, 20, 24, 12);
            SharedTextRenderer.DrawTerminalRun(layout, bounds, 12f, style, 6f);
            AssertEx.Equal(0, GUI.TextGroups.Count, "single plain span skips the group");
            AssertEx.Equal(bounds, GUI.TextLabels[0].Box, "label retains the old clip bounds");
            AssertEx.Equal(
                TextClipping.Clip,
                GUI.LabelClippings[0],
                "text clips within those bounds"
            );
            AssertEx.Equal(TextClipping.Overflow, style.clipping, "shared style is restored");

            Reset(new Texture2D());
            var sprite = InlineTextLayout.Cells("😀", 2, 6f, TextSpriteCatalog.Shared, c => true);
            SharedTextRenderer.DrawTerminalRun(sprite, bounds, 12f, style, 6f);
            AssertEx.Equal(1, GUI.TextGroups.Count, "sprites retain the group clip");
        }

        public static void MissingAtlasKeepsMeasuredPositions()
        {
            foreach (var missing in new[] { null, BaseContent.BadTex })
            {
                Reset(missing);
                var font = new Font();
                var style = new GUIStyle { font = font };
                var layout = InlineTextLayout.Proportional(
                    "😀x",
                    TextSpriteCatalog.Shared,
                    12f,
                    s => 4f
                );
                SharedTextRenderer.Draw(layout, new Rect(10, 20, 16, 12), 12f, style);
                AssertEx.Equal(
                    "\ufffd",
                    GUI.TextLabels[0].Text,
                    "missing artwork has visible replacement"
                );
                AssertEx.Equal(
                    12f,
                    GUI.TextLabels[1].Box.x,
                    "fallback does not move following text"
                );
                AssertEx.Equal(0, font.Prepared.Count, "missing atlas never requests emoji fonts");
                AssertEx.Equal(0, GUI.SpriteColors.Count, "BadTex is never sampled");
                AssertEx.Equal(16f, GUI.TextGroups[0].width, "paint clips to caller bounds");
                AssertEx.Equal(0, GUI.TextGroupDepth, "clip group restored");
            }
        }

        public static void KnownSpriteUsesAtlasSlotAndCenteredBounds()
        {
            var atlas = new Texture2D();
            Reset(atlas);
            var layout = InlineTextLayout.CellCluster("😀", 2, 10f, TextSpriteCatalog.Shared);
            SharedTextRenderer.Draw(layout, new Rect(30, 40, 20, 16), 16f, new GUIStyle());
            var draw = GUI.SpriteDraws[0];
            AssertEx.Equal(atlas, draw.Texture, "shipped atlas used");
            AssertEx.Equal(
                new Rect(2, 0, 16, 16),
                draw.Box,
                "glyph centers in its two-column advance inside clip group"
            );
            int slot = Array.IndexOf(TextSpriteData.Keys, "😀");
            AssertEx.True(slot >= 0, "known key has a generated slot");
            AssertEx.Equal(
                new Rect(
                    slot % TextSpriteData.AtlasColumns / (float)TextSpriteData.AtlasColumns,
                    (TextSpriteData.AtlasRows - slot / TextSpriteData.AtlasColumns - 1)
                        / (float)TextSpriteData.AtlasRows,
                    1f / TextSpriteData.AtlasColumns,
                    1f / TextSpriteData.AtlasRows
                ),
                draw.Uv,
                "draw samples expected atlas cell"
            );
        }

        public static void ArtworkPreservesOpacityAndGuiState()
        {
            Reset(new Texture2D());
            var font = new Font();
            var style = new GUIStyle { font = font };
            var layout = InlineTextLayout.Cells("😀", 2, 6f, TextSpriteCatalog.Shared, c => true);
            var old = GUI.color;
            var tint = new Color(0.2f, 0.3f, 0.4f, 0.5f);
            try
            {
                GUI.color = tint;
                SharedTextRenderer.Draw(layout, new Rect(0, 0, 12, 12), 12f, style);
                AssertEx.Equal(
                    new Color(1, 1, 1, 0.5f),
                    GUI.SpriteColors[0],
                    "artwork retains opacity without tint"
                );
                AssertEx.Equal(tint, GUI.color, "caller color restored");
                AssertEx.Equal(0, font.Prepared.Count, "atlas glyph bypasses dynamic fonts");
                GUI.FailSprite = true;
                AssertEx.Throws<InvalidOperationException>(
                    () => SharedTextRenderer.Draw(layout, new Rect(0, 0, 12, 12), 12f, style),
                    "draw failure"
                );
                AssertEx.Equal(tint, GUI.color, "color restored after failure");
                AssertEx.Equal(0, GUI.TextGroupDepth, "clip restored after failure");
            }
            finally
            {
                GUI.color = old;
                GUI.FailSprite = false;
            }
        }
    }
}
