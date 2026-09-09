namespace UnityEngine
{
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
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
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

    public static class Time
    {
        public static float realtimeSinceStartup;
        public static int frameCount;
    }
}
