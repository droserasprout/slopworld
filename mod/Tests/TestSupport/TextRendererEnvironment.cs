using System;
using System.Collections.Generic;

namespace UnityEngine
{
    // The text tests exercise axis-aligned GUI scaling and translation only.
    public struct Matrix4x4
    {
        public float m00,
            m11,
            m03,
            m13;
        public static Matrix4x4 identity => new Matrix4x4 { m00 = 1f, m11 = 1f };
    }

    static partial class GUI
    {
        public static Matrix4x4 matrix = Matrix4x4.identity;
        public static readonly List<Rect> TextGroups = new List<Rect>();
        public static readonly List<(Rect Box, string Text)> TextLabels =
            new List<(Rect, string)>();
        public static readonly List<TextClipping> LabelClippings = new List<TextClipping>();
        public static readonly List<int> LabelFontSizes = new List<int>();
        public static readonly List<Matrix4x4> LabelMatrices = new List<Matrix4x4>();
        public static Color color;
        public static readonly List<(Rect Box, Texture2D Texture, Rect Uv)> SpriteDraws =
            new List<(Rect, Texture2D, Rect)>();
        public static readonly List<Color> SpriteColors = new List<Color>();
        public static int TextGroupDepth;
        public static bool FailSprite;
        public static bool FailLabel;

        public static void BeginGroup(Rect bounds)
        {
            TextGroups.Add(bounds);
            TextGroupDepth++;
        }

        public static void EndGroup()
        {
            TextGroupDepth--;
        }

        public static void Label(Rect box, string text, GUIStyle style)
        {
            LabelMatrices.Add(matrix);
            box = new Rect(
                box.x * matrix.m00 + matrix.m03,
                box.y * matrix.m11 + matrix.m13,
                box.width * matrix.m00,
                box.height * matrix.m11
            );
            TextLabels.Add((box, text));
            LabelClippings.Add(style.clipping);
            LabelFontSizes.Add(style.fontSize);
            if (FailLabel)
                throw new InvalidOperationException("label failure");
        }

        public static void DrawTextureWithTexCoords(Rect box, Texture2D texture, Rect uv)
        {
            SpriteColors.Add(color);
            SpriteDraws.Add((box, texture, uv));
            if (FailSprite)
                throw new InvalidOperationException("draw failure");
        }
    }
}

namespace Verse
{
    static class BaseContent
    {
        public static readonly UnityEngine.Texture2D BadTex = new UnityEngine.Texture2D();
    }

    static class ContentFinder<T>
        where T : class
    {
        public static T Result;

        public static T Get(string path, bool reportFailure) => Result;
    }
}
