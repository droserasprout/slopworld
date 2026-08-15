using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SlopWorld
{
    // The mod only needs flat scalar TOML for its own small profile files and the daemon's
    // endpoint descriptor. Keeping this parser here avoids shipping another assembly beside
    // the mod, while still accepting the basic strings emitted by Rust's toml crate.
    public static class Toml
    {
        public static Dictionary<string, string> ParseFlat(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            foreach (string original in lines)
            {
                string line = original.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int equals = FindEquals(line);
                if (equals <= 0) throw new FormatException("TOML entry has no key/value separator");
                string key = line.Substring(0, equals).Trim();
                if (key.Length == 0) throw new FormatException("TOML entry has an empty key");
                string raw = line.Substring(equals + 1).Trim();
                values[key] = ParseValue(raw);
            }
            return values;
        }

        public static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '\\': result.Append("\\\\"); break;
                    case '"': result.Append("\\\""); break;
                    case '\b': result.Append("\\b"); break;
                    case '\t': result.Append("\\t"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\f': result.Append("\\f"); break;
                    case '\r': result.Append("\\r"); break;
                    default:
                        if (char.IsControl(c))
                            result.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            result.Append(c);
                        break;
                }
            }
            return result.Append('"').ToString();
        }

        static int FindEquals(string line)
        {
            bool quoted = false;
            bool escaped = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == '=') return i;
            }
            return -1;
        }

        static string ParseValue(string raw)
        {
            if (raw.Length == 0) throw new FormatException("TOML entry has an empty value");
            if (raw[0] == '"')
            {
                int at = 0;
                string value = ParseBasicString(raw, ref at);
                string tail = raw.Substring(at).TrimStart();
                if (tail.Length != 0 && tail[0] != '#')
                    throw new FormatException("TOML string has trailing characters");
                return value;
            }
            if (raw[0] == '\'')
            {
                int end = raw.IndexOf('\'', 1);
                if (end < 0) throw new FormatException("TOML literal string is unterminated");
                string tail = raw.Substring(end + 1).TrimStart();
                if (tail.Length != 0 && tail[0] != '#')
                    throw new FormatException("TOML literal string has trailing characters");
                return raw.Substring(1, end - 1);
            }

            int comment = raw.IndexOf('#');
            return (comment < 0 ? raw : raw.Substring(0, comment)).Trim();
        }

        static string ParseBasicString(string text, ref int at)
        {
            if (at >= text.Length || text[at] != '"')
                throw new FormatException("TOML value is not a basic string");
            at++;
            var value = new StringBuilder();
            while (at < text.Length)
            {
                char c = text[at++];
                if (c == '"') return value.ToString();
                if (c != '\\')
                {
                    if (c == '\n' || c == '\r') throw new FormatException("TOML string has a newline");
                    value.Append(c);
                    continue;
                }
                if (at >= text.Length) break;
                switch (text[at++])
                {
                    case 'b': value.Append('\b'); break;
                    case 't': value.Append('\t'); break;
                    case 'n': value.Append('\n'); break;
                    case 'f': value.Append('\f'); break;
                    case 'r': value.Append('\r'); break;
                    case '"': value.Append('"'); break;
                    case '\\': value.Append('\\'); break;
                    case 'u': value.Append(ParseCodePoint(text, ref at, 4)); break;
                    case 'U': value.Append(ParseCodePoint(text, ref at, 8)); break;
                    default: throw new FormatException("TOML string has an invalid escape");
                }
            }
            throw new FormatException("TOML string is unterminated");
        }

        static string ParseCodePoint(string text, ref int at, int digits)
        {
            if (at + digits > text.Length)
                throw new FormatException("TOML unicode escape is truncated");
            string hex = text.Substring(at, digits);
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                               out uint codePoint) || codePoint > 0x10ffff ||
                (codePoint >= 0xd800 && codePoint <= 0xdfff))
                throw new FormatException("TOML unicode escape is invalid");
            at += digits;
            return char.ConvertFromUtf32((int)codePoint);
        }
    }
}
