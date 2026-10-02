using System;
using System.Collections.Generic;

namespace UnityEngine
{
    static partial class GUI
    {
        public static readonly List<Rect> TextGroups = new List<Rect>();
        public static readonly List<(Rect Box, string Text)> TextLabels = new List<(Rect, string)>();
        public static readonly List<TextClipping> LabelClippings = new List<TextClipping>();
        public static Color color;
        public static readonly List<(Rect Box, Texture2D Texture, Rect Uv)> SpriteDraws = new List<(Rect, Texture2D, Rect)>();
        public static readonly List<Color> SpriteColors = new List<Color>();
        public static int TextGroupDepth;
        public static bool FailSprite;
        public static void BeginGroup(Rect bounds) { TextGroups.Add(bounds); TextGroupDepth++; }
        public static void EndGroup() { TextGroupDepth--; }
        public static void Label(Rect box, string text, GUIStyle style)
        {
            TextLabels.Add((box, text));
            LabelClippings.Add(style.clipping);
        }
        public static void DrawTextureWithTexCoords(Rect box, Texture2D texture, Rect uv)
        {
            SpriteColors.Add(color);
            SpriteDraws.Add((box, texture, uv));
            if (FailSprite) throw new InvalidOperationException("draw failure");
        }
    }
}

namespace Verse
{
    static class BaseContent
    {
        public static readonly UnityEngine.Texture2D BadTex = new UnityEngine.Texture2D();
    }
    static class ContentFinder<T> where T : class
    {
        public static T Result;
        public static T Get(string path, bool reportFailure) => Result;
    }
}
