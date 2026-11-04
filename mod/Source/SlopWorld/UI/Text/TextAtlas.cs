using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared texture lifetime and GUI state; callers own glyph geometry and tint.
    sealed class TextAtlas
    {
        readonly string _path;
        Texture2D _texture;
        bool _looked;

        public TextAtlas(string path) => _path = path;

        public Texture2D Texture
        {
            get
            {
                if (!_looked)
                {
                    _looked = true;
                    _texture = ContentFinder<Texture2D>.Get(_path, false);
                    if (_texture == BaseContent.BadTex)
                        _texture = null;
                    if (_texture == null)
                        Log.Warning("[SlopWorld] text atlas is missing: " + _path);
                }
                return _texture;
            }
        }

        public static void Draw(Texture2D texture, Rect rect, Rect uv, Color tint)
        {
            Color previous = GUI.color;
            try
            {
                GUI.color = tint;
                GUI.DrawTextureWithTexCoords(rect, texture, uv);
            }
            finally
            {
                GUI.color = previous;
            }
        }
    }
}
