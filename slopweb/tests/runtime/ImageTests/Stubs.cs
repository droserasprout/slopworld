namespace UnityEngine
{
    public enum TextureFormat
    {
        RGBA32,
    }

    public readonly record struct Color32(byte R, byte G, byte B, byte A);

    public sealed class Texture2D
    {
        public int Width,
            Height,
            Updates;
        public bool Readable,
            FailResize;
        public Color32[] Pixels = [];

        public bool Reinitialize(int width, int height, TextureFormat format, bool mipmaps)
        {
            if (FailResize)
                return false;
            Width = width;
            Height = height;
            return true;
        }

        public void SetPixels32(Color32[] pixels) => Pixels = pixels;

        public void Apply(bool mipmaps, bool makeNoLongerReadable) =>
            Readable = !makeNoLongerReadable;
    }
}

namespace BrowserAdapters
{
    public static class BrowserGui
    {
        public static void TextureApplied(UnityEngine.Texture2D texture) => texture.Updates++;
    }
}
