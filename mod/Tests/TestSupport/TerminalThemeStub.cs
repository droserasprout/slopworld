using UnityEngine;

namespace SlopWorld
{
    public class TerminalTheme
    {
        public readonly Color Fg, Bg;
        public readonly Color[] Ansi;

        static readonly TerminalTheme _instance = new TerminalTheme();
        public static TerminalTheme Current => _instance;

        TerminalTheme()
        {
            Fg = new Color(0.8f, 0.8f, 0.8f);
            Bg = new Color(0f, 0f, 0f);
            Ansi = new Color[]
            {
                new Color(0f, 0f, 0f),           // 0  black
                new Color(0.8f, 0f, 0f),          // 1  red
                new Color(0f, 0.8f, 0f),          // 2  green
                new Color(0.8f, 0.8f, 0f),        // 3  yellow
                new Color(0f, 0f, 0.8f),          // 4  blue
                new Color(0.8f, 0f, 0.8f),        // 5  magenta
                new Color(0f, 0.8f, 0.8f),        // 6  cyan
                new Color(0.75f, 0.75f, 0.75f),   // 7  white
                new Color(0.5f, 0.5f, 0.5f),      // 8  bright black
                new Color(1f, 0f, 0f),            // 9  bright red
                new Color(0f, 1f, 0f),            // 10 bright green
                new Color(1f, 1f, 0f),            // 11 bright yellow
                new Color(0f, 0f, 1f),            // 12 bright blue
                new Color(1f, 0f, 1f),            // 13 bright magenta
                new Color(0f, 1f, 1f),            // 14 bright cyan
                new Color(1f, 1f, 1f),            // 15 bright white
            };
        }
    }
}
