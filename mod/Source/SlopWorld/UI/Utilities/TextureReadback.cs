using UnityEngine;

namespace SlopWorld
{
    // Bundle textures are not readable on the CPU. Keep the temporary GPU target and CPU copy
    // cleanup here so every caller restores the active target even when readback fails.
    internal static class TextureReadback
    {
        public static Color[] ReadBack(Texture2D source)
        {
            var target = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                copy = new Texture2D(source.width, source.height, TextureFormat.ARGB32, false);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                copy.Apply();
                return copy.GetPixels();
            }
            finally
            {
                try
                {
                    if (copy != null) Object.Destroy(copy);
                }
                finally
                {
                    try
                    {
                        RenderTexture.active = previous;
                    }
                    finally
                    {
                        RenderTexture.ReleaseTemporary(target);
                    }
                }
            }
        }
    }
}
