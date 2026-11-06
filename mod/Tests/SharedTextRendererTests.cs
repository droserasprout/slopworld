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
            foreach (var owner in new[] { typeof(SharedTextRenderer), typeof(TextSymbolRenderer) })
            {
                var cache = owner
                    .GetField("Atlas", BindingFlags.Static | BindingFlags.NonPublic)
                    .GetValue(null);
                typeof(TextAtlas)
                    .GetField("_looked", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(cache, false);
                typeof(TextAtlas)
                    .GetField("_texture", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(cache, null);
            }
            ContentFinder<Texture2D>.Result = atlas;
            GUI.TextGroups.Clear();
            GUI.TextLabels.Clear();
            GUI.LabelClippings.Clear();
            GUI.LabelFontSizes.Clear();
            GUI.LabelMatrices.Clear();
            GUI.color = new Color(1, 1, 1, 1);
            GUI.LabelColors.Clear();
            GUI.SpriteColors.Clear();
            GUI.SpriteDraws.Clear();
            GUI.TextGroupDepth = 0;
            GUI.FailSprite = false;
            GUI.FailLabel = false;
            GUI.matrix = Matrix4x4.identity;
        }

        public static void TerminalForegroundChangesBetweenRunsAndRestoresCallerTint()
        {
            Reset(new Texture2D());
            Color previous = GUI.color;
            var tint = new Color(0.8f, 0.6f, 0.4f, 0.5f);
            var red = new Color(1f, 0.1f, 0.2f, 0.8f);
            var blue = new Color(0.2f, 0.3f, 1f, 0.6f);
            var style = new GUIStyle();
            var plain = InlineTextLayout.Cells("a", 1, 6f, TextSpriteCatalog.Shared);
            var mixed = InlineTextLayout.Cells("a😀b", 4, 6f, TextSpriteCatalog.Shared);
            var bounds = new Rect(0, 0, 30, 14);
            try
            {
                GUI.color = tint;
                style.normal.textColor = red;
                SharedTextRenderer.DrawTerminalRun(plain, bounds, 14f, style);
                AssertEx.Equal(
                    tint * red,
                    GUI.LabelColors[0],
                    "plain glyph retains foreground and caller opacity"
                );
                AssertEx.Equal(tint, GUI.color, "plain run restores caller tint");
                style.normal.textColor = blue;
                SharedTextRenderer.DrawTerminalRun(mixed, bounds, 14f, style);
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
                    () => SharedTextRenderer.DrawTerminalRun(plain, bounds, 14f, style),
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

        public static void PlainTerminalRunUsesScalarColumnsAndGroupClip()
        {
            Reset(null);
            var style = new GUIStyle { clipping = TextClipping.Overflow };
            var layout = InlineTextLayout.Cells("abc", 3, 6f, TextSpriteCatalog.Shared);
            var bounds = new Rect(10, 20, 24, 12);
            SharedTextRenderer.DrawTerminalRun(layout, bounds, 12f, style);
            AssertEx.Equal(1, GUI.TextGroups.Count, "scalar spans share the terminal clip group");
            AssertEx.Equal(3, GUI.TextLabels.Count, "each scalar is drawn at its own column");
            AssertEx.Equal(new Rect(0, 0, 6, 12), GUI.TextLabels[0].Box, "first scalar bounds");
            AssertEx.Equal(new Rect(6, 0, 6, 12), GUI.TextLabels[1].Box, "second scalar bounds");
            AssertEx.Equal(new Rect(12, 0, 6, 12), GUI.TextLabels[2].Box, "third scalar bounds");
            AssertEx.Equal(
                TextClipping.Clip,
                GUI.LabelClippings[0],
                "each scalar clips to its selected cell"
            );
            AssertEx.Equal(TextClipping.Overflow, style.clipping, "shared style is restored");

            Reset(new Texture2D());
            var sprite = InlineTextLayout.Cells("😀", 2, 6f, TextSpriteCatalog.Shared);
            SharedTextRenderer.DrawTerminalRun(sprite, bounds, 12f, style);
            AssertEx.Equal(1, GUI.TextGroups.Count, "sprites retain the group clip");
        }

        public static void TextSymbolsPreferSupportedTerminalGlyphsBeforeAtlas()
        {
            var font = new Font();
            var style = new GUIStyle { font = font, fontSize = 16 };
            foreach (string text in new[] { "⚠", "⚠\ufe0e", "☠", "❤", "©" })
            {
                Reset(new Texture2D());
                font.SupportsCharacter = _ => true;
                var layout = InlineTextLayout.CellCluster(text, 1, 8f, TextSpriteCatalog.Shared);
                SharedTextRenderer.DrawTerminalRun(layout, new Rect(0, 0, 8, 18), 18, style);
                AssertEx.Equal(0, GUI.SpriteDraws.Count, "supported text uses terminal font");
                AssertEx.Equal(text, GUI.TextLabels[0].Text, "original text retained");
                Reset(new Texture2D());
                font.SupportsCharacter = _ => false;
                SharedTextRenderer.DrawTerminalRun(layout, new Rect(0, 0, 8, 18), 18, style);
                AssertEx.Equal(1, GUI.SpriteDraws.Count, "missing font glyph uses atlas");
                AssertEx.Equal(0, GUI.TextLabels.Count, "missing glyph avoids native tofu");
            }
            font.SupportsCharacter = _ => true;
            foreach (string text in new[] { "⚠️", "☠️", "❤️", "❤️‍🩹", "🖥" })
            {
                Reset(new Texture2D());
                var layout = InlineTextLayout.CellCluster(text, 2, 8f, TextSpriteCatalog.Shared);
                SharedTextRenderer.DrawTerminalRun(layout, new Rect(0, 0, 16, 18), 18, style);
                AssertEx.Equal(
                    1,
                    GUI.SpriteDraws.Count,
                    "emoji and supplementary fallback retain artwork"
                );
                AssertEx.Equal(0, GUI.TextLabels.Count, "native font does not replace artwork");
            }
        }

        public static void NativeCoverageWithoutLoadedInkFallsBackAndCanRecover()
        {
            var font = new Font();
            var style = new GUIStyle { font = font, fontSize = 18 };
            var layout = InlineTextLayout.CellCluster("⚠", 1, 8, TextSpriteCatalog.Shared);
            foreach (FontStyle face in new[] { FontStyle.Normal, FontStyle.Bold, FontStyle.Italic })
            foreach (bool empty in new[] { false, true })
            {
                style.fontStyle = face;
                Reset(new Texture2D());
                font.CharacterMetrics = (character, size, faceStyle) =>
                {
                    AssertEx.True(
                        font.Prepared.Contains("⚠"),
                        "fallback glyph warmed before metrics query"
                    );
                    AssertEx.Equal(18, size, "query uses drawing size");
                    AssertEx.Equal(style.fontStyle, faceStyle, "query uses drawing style");
                    return empty ? new CharacterInfo() : (CharacterInfo?)null;
                };
                SharedTextRenderer.DrawTerminalRun(layout, new Rect(0, 0, 8, 20), 20, style);
                AssertEx.Equal(
                    1,
                    GUI.SpriteDraws.Count,
                    "coverage without usable ink uses fallback"
                );
                AssertEx.Equal(0, GUI.TextLabels.Count, "invisible native label is not selected");
            }
            Reset(new Texture2D());
            font.CharacterMetrics = (_, _, _) => new CharacterInfo { maxX = 8, maxY = 14 };
            SharedTextRenderer.DrawTerminalRun(layout, new Rect(0, 0, 8, 20), 20, style);
            AssertEx.Equal(0, GUI.SpriteDraws.Count, "native glyph used after atlas recovery");
            AssertEx.Equal("⚠", GUI.TextLabels[0].Text, "original scalar preserved");
        }

        public static void AllTextDefaultSymbolsKeepInkTintAndCellBounds()
        {
            Reset(new Texture2D());
            AssertEx.Equal(207, TextSymbolData.Codepoints.Length, "BMP and supplementary coverage");
            AssertEx.True(TextSymbolRenderer.Slot("☠") >= 0, "skull uses measured monochrome ink");
            var style = new GUIStyle { clipping = TextClipping.Overflow };
            style.normal.textColor = new Color(0.25f, 0.5f, 0.75f, 1f);
            foreach (int cp in TextSymbolData.Codepoints)
            {
                string text = char.ConvertFromUtf32(cp);
                foreach (string form in new[] { text, text + "\ufe0e" })
                {
                    GUI.SpriteDraws.Clear();
                    GUI.SpriteColors.Clear();
                    var layout = InlineTextLayout.CellCluster(
                        form,
                        1,
                        8f,
                        TextSpriteCatalog.Shared
                    );
                    SharedTextRenderer.DrawTerminalRun(layout, new Rect(10, 20, 8, 16), 16f, style);
                    AssertEx.Equal(1, GUI.SpriteDraws.Count, "monochrome font ink is visible");
                    var box = GUI.SpriteDraws[0].Box;
                    int at = TextSymbolRenderer.Slot(form) * 4;
                    float sourceWidth = TextSymbolData.FontBounds[at + 2];
                    float sourceHeight = TextSymbolData.FontBounds[at + 3];
                    AssertEx.True(
                        Math.Abs(box.width / box.height - sourceWidth / sourceHeight) < 0.0001f,
                        "font proportions preserved: " + form
                    );
                    AssertEx.True(
                        box.x >= 10
                            && box.y >= 20
                            && box.x + box.width <= 18
                            && box.y + box.height <= 36,
                        "ink stays in selected cell"
                    );
                    AssertEx.Equal(
                        style.normal.textColor,
                        GUI.SpriteColors[0],
                        "font ink uses terminal foreground"
                    );
                    AssertEx.Equal(form, layout.Spans[0].Text, "copy text unchanged");
                }
                AssertEx.True(
                    TextSymbolRenderer.Slot(text + "\ufe0f") < 0,
                    "emoji remains on color path"
                );
            }
            AssertEx.Equal(0, GUI.TextLabels.Count, "unsupported scalars never reach Unity text");
            AssertEx.Equal(TextClipping.Overflow, style.clipping, "style restored");
        }

        public static void MonochromeFontMetricsPreserveRelativeSizeAndBaseline()
        {
            var cell = new Rect(0, 0, 20, 20);
            var capital = TextSymbolRenderer.GlyphRect(new Rect(0, -60, 36, 60), cell, 16, 14);
            var small = TextSymbolRenderer.GlyphRect(new Rect(0, -30, 18, 30), cell, 16, 14);
            AssertEx.Equal(
                capital.height / 2,
                small.height,
                "small ink is not enlarged to row height"
            );
            AssertEx.Equal(14f, capital.y + capital.height, "capital rests on baseline");
            AssertEx.Equal(14f, small.y + small.height, "small glyph shares baseline");
            var tallerRow = TextSymbolRenderer.GlyphRect(
                new Rect(0, -60, 36, 60),
                new Rect(0, 0, 20, 30),
                16,
                14
            );
            AssertEx.Equal(capital, tallerRow, "line spacing does not resize font ink");
            var wide = TextSymbolRenderer.GlyphRect(
                new Rect(-6, -60, 120, 60),
                new Rect(0, 0, 8, 20),
                16,
                14
            );
            AssertEx.Equal(2f, wide.width / wide.height, "oversized glyph shrinks uniformly");
            AssertEx.Equal(0f, wide.x, "negative bearing stays inside cells");
        }

        public static void NativeSymbolsFitTheirCellsAndRestoreGuiState()
        {
            var matrix = new Matrix4x4
            {
                m00 = 2,
                m11 = 2,
                m03 = 3,
                m13 = 5,
            };
            foreach (string text in new[] { "⚠", "☠", "❤", "∑", "∞", "√" })
            {
                Reset(null);
                GUI.matrix = matrix;
                var style = new GUIStyle
                {
                    font = new Font(),
                    fontSize = 16,
                    clipping = TextClipping.Overflow,
                };
                var layout = InlineTextLayout.Cells(text + "x", 2, 4f, TextSpriteCatalog.Shared);
                Action draw = () =>
                    SharedTextRenderer.DrawTerminalRun(layout, new Rect(10, 20, 8, 18), 18, style);
                try
                {
                    draw();
                    AssertEx.Equal(
                        9,
                        GUI.LabelFontSizes[0],
                        "seven-unit glyph fits four-unit cell"
                    );
                    AssertEx.Equal(16, GUI.LabelFontSizes[1], "following text retains font size");
                    AssertEx.Equal(text, GUI.TextLabels[0].Text, "original symbol preserved");
                    AssertEx.Equal(
                        11f,
                        GUI.TextLabels[1].Box.x,
                        "following cell keeps its scaled column"
                    );
                    AssertEx.Equal(
                        8f,
                        GUI.TextLabels[0].Box.width,
                        "glyph clips to scaled cell width"
                    );
                    AssertEx.Equal(
                        new Rect(3, 15.5f, 8, 25.5f),
                        GUI.TextLabels[0].Box,
                        "fitted glyph preserves baseline under caller scaling"
                    );
                    AssertEx.Equal(
                        TextClipping.Clip,
                        GUI.LabelClippings[0],
                        "fallback bearings clipped"
                    );
                    AssertEx.Equal(
                        matrix,
                        GUI.LabelMatrices[0],
                        "drawing never changes caller matrix"
                    );
                    AssertEx.Equal(0, GUI.SpriteDraws.Count, "supported symbol uses native font");
                    AssertRestored();
                    GUI.FailLabel = true;
                    AssertEx.Throws<InvalidOperationException>(draw, "font draw failure");
                    AssertRestored();
                }
                finally
                {
                    GUI.matrix = Matrix4x4.identity;
                    GUI.FailLabel = false;
                }

                void AssertRestored()
                {
                    AssertEx.Equal(matrix, GUI.matrix, "caller transform restored");
                    AssertEx.Equal(16, style.fontSize, "shared font size restored");
                    AssertEx.Equal(
                        TextClipping.Overflow,
                        style.clipping,
                        "shared clipping restored"
                    );
                    AssertEx.Equal(0, GUI.TextGroupDepth, "clip group restored");
                }
            }
        }

        public static void MissingSymbolAtlasPreservesOccupiedCells()
        {
            Reset(null);
            var layout = InlineTextLayout.CellCluster("🖥", 2, 8f, TextSpriteCatalog.Shared);
            SharedTextRenderer.DrawTerminalRun(
                layout,
                new Rect(10, 20, 16, 18),
                18,
                new GUIStyle()
            );
            AssertEx.Equal(
                "\ufffd",
                GUI.TextLabels[0].Text,
                "missing atlas gives visible replacement"
            );
            AssertEx.Equal(16f, GUI.TextLabels[0].Box.width, "replacement retains two cells");
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

        public static void JoinedEmojiRemainOneColoredSprite()
        {
            foreach (
                string text in new[] { "🧎🏽‍♂️‍➡️", "🧎🏽‍♂‍➡", "👨‍❤️‍💋‍👨", "🧑🏿‍❤️‍💋‍🧑🏻" }
            )
            {
                Reset(new Texture2D());
                var layout = InlineTextLayout.CellCluster(text, 2, 8f, TextSpriteCatalog.Shared);
                SharedTextRenderer.DrawTerminalRun(
                    layout,
                    new Rect(10, 20, 16, 18),
                    18f,
                    new GUIStyle()
                );
                AssertEx.Equal(
                    1,
                    GUI.SpriteDraws.Count,
                    "whole joined sequence selects artwork: " + text
                );
                AssertEx.Equal(0, GUI.TextLabels.Count, "no joiner reaches font fallback");
                AssertEx.Equal(
                    new Color(1, 1, 1, 1),
                    GUI.SpriteColors[0],
                    "emoji preserves artwork color"
                );
                AssertEx.Equal(
                    text,
                    layout.Spans[0].Text,
                    "original sequence preserved for copying"
                );
            }
        }

        public static void TallEmojiUseLineHeightInsideOneColumn()
        {
            Reset(new Texture2D());
            var layout = InlineTextLayout.CellCluster("🏌🏽", 1, 8f, TextSpriteCatalog.Shared);
            SharedTextRenderer.DrawTerminalRun(layout, new Rect(0, 0, 8, 18), 18f, new GUIStyle());
            var box = GUI.SpriteDraws[0].Box;
            AssertEx.True(box.height > 8f, "golfer is not constrained to a tiny cell-width square");
            AssertEx.True(box.width <= 8f && box.height <= 18f, "ink remains inside selection");
            AssertEx.Equal(8f, layout.Width, "daemon cell advance unchanged");

            // A narrow, tall ink rectangle in a square tile must preserve aspect
            // and use the correct subrectangle, including the texture Y inversion.
            TextSpriteGeometry.Fit(
                0x1C08020C,
                5,
                4,
                3,
                32,
                new Rect(10, 20, 8, 16),
                out box,
                out var uv
            );
            AssertEx.True(box.height > 14f && box.height <= 16f, "tall ink uses row height");
            AssertEx.True(
                System.Math.Abs(box.width / box.height - 8f / 28f) < 0.0001f,
                "aspect preserved"
            );
            AssertEx.Equal(
                new Rect(44f / 128f, 34f / 96f, 8f / 128f, 28f / 96f),
                uv,
                "cropped atlas UV"
            );
        }

        public static void ArtworkPreservesOpacityAndGuiState()
        {
            Reset(new Texture2D());
            var font = new Font();
            var style = new GUIStyle { font = font };
            var layout = InlineTextLayout.Cells("😀", 2, 6f, TextSpriteCatalog.Shared);
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
