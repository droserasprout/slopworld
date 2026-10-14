using System.IO;
using UnityEngine;

namespace SlopWorld
{
    // Owns the texture until bounded decoding and setup succeed. Callers own returned
    // textures, request lifetimes, and any additional resource admission or cleanup.
    static class BoundedImageDecoder
    {
        public static Texture2D Decode(
            byte[] bytes,
            string textureName,
            string sizeError,
            string decodeError
        )
        {
            if (!MarkdownImageHeader.TrySize(bytes, out int width, out int height))
                throw new InvalidDataException(sizeError);

            Texture2D texture = null;
            try
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (
                    !texture.LoadImage(bytes, true)
                    || texture.width != width
                    || texture.height != height
                )
                    throw new InvalidDataException(decodeError);
                texture.name = textureName;
                texture.hideFlags = HideFlags.HideAndDontSave;
                var decoded = texture;
                texture = null;
                return decoded;
            }
            finally
            {
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
