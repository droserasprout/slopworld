using UnityEngine;

namespace SlopWorld
{
    // Shared RGB/RGBA palette decoding; callers own setting-specific validation.
    public static class HexColor
    {
        public static Color Hex(string s, float a = 1f)
        {
            if (TryHex(s, out var c)) { c.a *= a; return c; }
            return new Color(1f, 1f, 1f, a);
        }

        public static bool TryHex(string s, out Color c)
        {
            c = Color.white;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s.Length > 0 && s[0] == '#') s = s.Substring(1);
            if (s.Length != 6 && s.Length != 8) return false;
            uint v = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int d = Digit(s[i]);
                if (d < 0) return false;
                v = v * 16u + (uint)d;
            }
            if (s.Length == 6) v = (v << 8) | 0xFF;
            c = new Color(((v >> 24) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f,
                          ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
            return true;
        }

        static int Digit(char ch)
        {
            if (ch >= '0' && ch <= '9') return ch - '0';
            if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
            if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
            return -1;
        }

    }
}
