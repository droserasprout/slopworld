using UnityEngine;
using static SlopWorld.MenuBackgroundTuning;

namespace SlopWorld
{
    // Shared layer selection and GUI-space displacement; Eco projects these into world space.
    internal static class MenuBackgroundLayers
    {
        internal const int Count = 2;

        internal readonly struct Layer
        {
            internal readonly Texture2D Texture;
            internal readonly Vector2 Offset;
            internal readonly float Opacity;

            internal Layer(Texture2D texture, Vector2 offset, float opacity)
            {
                Texture = texture;
                Offset = offset;
                Opacity = opacity;
            }
        }

        // An opaque previous frame plus a half-alpha current frame gives both equal weight
        // without letting the underlying background dim the pair.
        internal static Layer Get(Texture2D current, int index) =>
            index == 0
                ? new Layer(MenuBackground.Previous ?? current, -LayerOffset, 1f)
                : new Layer(current, LayerOffset, 0.5f);

        internal static void DrawLayers(Rect rect, Texture texture) =>
            DrawLayers(rect, texture, ScaleMode.StretchToFill, true);

        internal static void DrawLayers(Rect rect, Texture texture, ScaleMode scaleMode) =>
            DrawLayers(rect, texture, scaleMode, true);

        internal static void DrawLayers(
            Rect rect,
            Texture texture,
            ScaleMode scaleMode,
            bool alphaBlend
        ) => DrawLayers(rect, texture, scaleMode, alphaBlend, 0f);

        internal static void DrawLayers(
            Rect rect,
            Texture texture,
            ScaleMode scaleMode,
            bool alphaBlend,
            float imageAspect
        )
        {
            if (!(texture is Texture2D frame) || !MenuBackground.IsOurs(frame))
            {
                GUI.DrawTexture(rect, texture, scaleMode, alphaBlend, imageAspect);
                return;
            }

            Color saved = GUI.color;
            try
            {
                for (int i = 0; i < Count; i++)
                {
                    Layer layer = Get(frame, i);
                    GUI.color = new Color(saved.r, saved.g, saved.b, saved.a * layer.Opacity);
                    Rect shifted = rect;
                    shifted.position += layer.Offset;
                    GUI.DrawTexture(shifted, layer.Texture, scaleMode, true, imageAspect);
                }
            }
            finally
            {
                GUI.color = saved;
            }
        }
    }
}
