using UnityEngine;

namespace Verse
{
    static partial class Text
    {
        public static GameFont Font;
        static readonly GUIStyle Small = new GUIStyle { font = new Font() };
        static readonly GUIStyle Medium = new GUIStyle
        {
            font = new Font { fontSize = 24, ascent = 18, Height = 27 },
        };
        public static GUIStyle CurFontStyle => Font == GameFont.Medium ? Medium : Small;
    }
}

namespace SlopWorld
{
    // Only engine/resource boundaries are substituted; styles and block flow are production.
    static class TerminalFont
    {
        public static int Rev;
        public static GUIStyle Style = new GUIStyle
        {
            font = new Font { Monospace = true, ascent = 9, Height = 20 },
            fontSize = 16,
        };
    }
    sealed class MarkdownResourceStore
    {
        public Texture2D ImageFor(InlineRun run) => null;
    }
    static partial class UiTheme { public const float TinyH = 11f; }
}
