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
            typeof(SharedTextRenderer).GetField("_looked", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, false);
            typeof(SharedTextRenderer).GetField("_atlas", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, null);
            ContentFinder<Texture2D>.Result = atlas;
            GUI.TextGroups.Clear();
            GUI.TextLabels.Clear();
            GUI.SpriteColors.Clear();
            GUI.TextGroupDepth = 0;
            GUI.FailSprite = false;
        }

        public static void MissingAtlasKeepsMeasuredPositions()
        {
            foreach (var missing in new[] { null, BaseContent.BadTex })
            {
                Reset(missing);
                var font = new Font();
                var style = new GUIStyle { font = font };
                var layout = InlineTextLayout.Proportional("😀x", TextSpriteCatalog.Shared, 12f, s => 4f);
                SharedTextRenderer.Draw(layout, new Rect(10, 20, 16, 12), 12f, style);
                AssertEx.Equal("\ufffd", GUI.TextLabels[0].Text, "missing artwork has visible replacement");
                AssertEx.Equal(12f, GUI.TextLabels[1].Box.x, "fallback does not move following text");
                AssertEx.Equal(0, font.Prepared.Count, "missing atlas never requests emoji fonts");
                AssertEx.Equal(0, GUI.SpriteColors.Count, "BadTex is never sampled");
                AssertEx.Equal(16f, GUI.TextGroups[0].width, "paint clips to caller bounds");
                AssertEx.Equal(0, GUI.TextGroupDepth, "clip group restored");
            }
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
                AssertEx.Equal(new Color(1, 1, 1, 0.5f), GUI.SpriteColors[0], "artwork retains opacity without tint");
                AssertEx.Equal(tint, GUI.color, "caller color restored");
                AssertEx.Equal(0, font.Prepared.Count, "atlas glyph bypasses dynamic fonts");
                GUI.FailSprite = true;
                AssertEx.Throws<InvalidOperationException>(() =>
                    SharedTextRenderer.Draw(layout, new Rect(0, 0, 12, 12), 12f, style), "draw failure");
                AssertEx.Equal(tint, GUI.color, "color restored after failure");
                AssertEx.Equal(0, GUI.TextGroupDepth, "clip restored after failure");
            }
            finally { GUI.color = old; GUI.FailSprite = false; }
        }
    }
}
