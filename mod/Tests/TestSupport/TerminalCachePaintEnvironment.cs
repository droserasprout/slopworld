using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    // Simulate GUI commands being baked only when their draw target is closed.
    public sealed class RenderTexture
    {
        static RenderTexture _active;
        public readonly List<string> Pending = new List<string>();
        public string[] Pixels = System.Array.Empty<string>();
        public Rect BackgroundBounds;
        public Color Background;

        public static RenderTexture active
        {
            get => _active;
            set
            {
                if (_active == value)
                    return;
                if (_active != null && _active.Pending.Count > 0)
                {
                    _active.Pixels = _active.Pending.ToArray();
                    _active.Pending.Clear();
                }
                _active = value;
            }
        }
    }
}

namespace Verse
{
    static partial class Widgets
    {
        public static void DrawBoxSolid(Rect bounds, Color background)
        {
            RenderTexture.active.BackgroundBounds = bounds;
            RenderTexture.active.Background = background;
            RenderTexture.active.Pending.Add("background");
        }
    }
}
