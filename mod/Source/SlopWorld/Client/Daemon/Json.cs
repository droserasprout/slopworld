using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SlopWorld
{
    // RimWorld ships no JSON library and pulling one in would mean shipping a second
    // DLL. It only has to handle what slopd emits.
    public class JVal
    {
        public Dictionary<string, JVal> Obj;
        public List<JVal> Arr;
        public string Str;
        public double Num;
        public bool Bool;
        public bool IsNull;

        public static readonly JVal Null = new JVal { IsNull = true };

        public JVal this[string key] =>
            Obj != null && Obj.TryGetValue(key, out var v) ? v : Null;

        public JVal this[int i] =>
            Arr != null && i >= 0 && i < Arr.Count ? Arr[i] : Null;

        public int Count => Arr?.Count ?? 0;

        public string AsString(string fallback = "") => Str ?? fallback;
        public int AsInt(int fallback = 0) => Str == null && !IsNull ? (int)Num : fallback;
        public long AsLong(long fallback = 0) => Str == null && !IsNull ? (long)Num : fallback;
        public bool AsBool(bool fallback = false) => IsNull ? fallback : Bool;
        public float AsFloat(float fallback = 0f) => Str == null && !IsNull ? (float)Num : fallback;

        public IEnumerable<JVal> Items => Arr ?? new List<JVal>();

        public static JVal Parse(string s)
        {
            int i = 0;
            var v = ParseValue(s, ref i);
            return v;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static JVal ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return Null;

            switch (s[i])
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return new JVal { Str = ParseString(s, ref i) };
                // Matched rather than assumed: advancing past a literal that is not there
                // lands the cursor mid-token, and the container loops read whatever it
                // points at next as the separator - which turns one truncated value into a
                // silently wrong object rather than a missing one.
                case 't': return Literal(s, ref i, "true") ? new JVal { Bool = true } : Null;
                case 'f': return Literal(s, ref i, "false") ? new JVal { Bool = false } : Null;
                case 'n': Literal(s, ref i, "null"); return Null;
                default: return ParseNumber(s, ref i);
            }
        }

        static bool Literal(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }

        static JVal ParseObject(string s, ref int i)
        {
            var o = new Dictionary<string, JVal>();
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return new JVal { Obj = o }; }

            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') break;
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                o[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return new JVal { Obj = o };
        }

        static JVal ParseArray(string s, ref int i)
        {
            var a = new List<JVal>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return new JVal { Arr = a }; }

            while (i < s.Length)
            {
                a.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return new JVal { Arr = a };
        }

        static string ParseString(string s, ref int i)
        {
            i++; // opening quote
            // Object keys and plain terminal rows need no escape decoding. Avoid the
            // builder and its growing buffers until an actual escape is encountered.
            int start = i;
            while (i < s.Length && s[i] != '"' && s[i] != '\\') i++;
            if (i == s.Length || s[i] == '"')
            {
                string plain = s.Substring(start, i - start);
                i++;
                return plain;
            }
            var sb = new StringBuilder();
            sb.Append(s, start, i - start);
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    switch (s[i])
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case '/': sb.Append('/'); break;
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case 'u':
                            if (i + 4 < s.Length)
                            {
                                var hex = s.Substring(i + 1, 4);
                                if (ushort.TryParse(hex, NumberStyles.HexNumber,
                                                    CultureInfo.InvariantCulture, out var cp))
                                    sb.Append((char)cp);
                                i += 4;
                            }
                            break;
                        default: sb.Append(s[i]); break;
                    }
                    i++;
                }
                else
                {
                    sb.Append(s[i]);
                    i++;
                }
            }
            i++; // closing quote
            return sb.ToString();
        }

        static JVal ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' ||
                                    s[i] == '.' || s[i] == 'e' || s[i] == 'E'))
                i++;
            double.TryParse(s.Substring(start, i - start), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var d);
            return new JVal { Num = d };
        }

        // Escapes a string and wraps it in quotes, ready to drop into a request body.
        public static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == 0x7f)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        public static string B(bool b) => b ? "true" : "false";
    }
}
