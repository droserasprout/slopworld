namespace UnityEngine
{
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum TextAnchor { UpperLeft }
    public enum TextClipping { Overflow, Clip }
    public class RectOffset { public RectOffset(int l, int r, int t, int b) { } }
    public class Font
    {
        public int fontSize = 16;
        public float ascent = 12f;
        public float Height = 18f;
        public bool Monospace;
    }
    public class Texture2D { public int width = 20, height = 20; }
    public class GUIStyleState { public Color textColor; }
    public class GUIStyle
    {
        public Font font;
        public int fontSize;
        public FontStyle fontStyle;
        public TextAnchor alignment;
        public TextClipping clipping;
        public RectOffset margin, padding;
        public bool richText, wordWrap;
        public GUIStyleState normal = new GUIStyleState();
        public GUIStyle() { }
        public GUIStyle(GUIStyle source)
        {
            font = source.font;
            fontSize = source.fontSize;
            fontStyle = source.fontStyle;
            normal.textColor = source.normal.textColor;
        }
        float Scale => font == null ? 1f : (fontSize > 0 ? fontSize : font.fontSize) / (float)font.fontSize;
        public float lineHeight => font == null ? 1f : font.Height * Scale;
        public Vector2 CalcSize(GUIContent content)
        {
            float width = 0f;
            foreach (char c in content.text)
                width += font == null ? 1f : font.Monospace ? 8f : c == 'i' ? 3f : c == 'W' ? 12f : 7f;
            return new Vector2(width * Scale, lineHeight);
        }
    }

    public class GUIContent
    {
        public string text;
        public GUIContent(string value) { text = value; }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float sqrMagnitude => x * x + y * y;
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
    }

    public class Event
    {
        public int button, clickCount;
        public Vector2 mousePosition;
    }

    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
    }

    public struct Color
    {
        public float r, g, b, a;

        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }

        public static bool operator ==(Color a, Color b) =>
            a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        public static bool operator !=(Color a, Color b) => !(a == b);

        public static Color Lerp(Color a, Color b, float t)
        {
            float s = 1f - t;
            return new Color(a.r * s + b.r * t, a.g * s + b.g * t,
                             a.b * s + b.b * t, a.a * s + b.a * t);
        }

        public override bool Equals(object obj) => obj is Color c && this == c;
        public override int GetHashCode() => r.GetHashCode() ^ g.GetHashCode() ^ b.GetHashCode() ^ a.GetHashCode();
        public override string ToString() => $"({r:F3},{g:F3},{b:F3},{a:F3})";

        public static Color white => new Color(1f, 1f, 1f);
    }

    public static class Mathf
    {
        public static bool Approximately(float a, float b) => System.Math.Abs(a - b) < .0001f;
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Ceil(float value) => (float)System.Math.Ceiling(value);
        public static float Clamp(float v, float min, float max) =>
            v < min ? min : v > max ? max : v;
        public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
    }

    public struct Rect
    {
        public float x, y, width, height;

        public Rect(float x, float y, float width, float height)
        {
            this.x = x; this.y = y; this.width = width; this.height = height;
        }

        public float xMax => x + width;
        public float yMax => y + height;
    }

    public static class GUIUtility
    {
        public static Vector2 Origin;
        public static Vector2 GUIToScreenPoint(Vector2 p) => new Vector2(p.x + Origin.x, p.y + Origin.y);
    }

    public static class Time
    {
        public static float realtimeSinceStartup;
        public static int frameCount;
    }
}
