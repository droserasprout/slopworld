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
        public bool IsBool;
        public bool IsNumber;

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
                case 't':
                    return Literal(s, ref i, "true")
                    ? new JVal { Bool = true, IsBool = true } : Null;
                case 'f':
                    return Literal(s, ref i, "false")
                    ? new JVal { Bool = false, IsBool = true } : Null;
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
            return new JVal { Num = d, IsNumber = true };
        }

        public static bool Equivalent(JVal left, JVal right)
        {
            if (left == null) left = Null;
            if (right == null) right = Null;
            if (left.IsNull || right.IsNull) return left.IsNull && right.IsNull;
            if (left.Obj != null || right.Obj != null)
            {
                if (left.Obj == null || right.Obj == null || left.Obj.Count != right.Obj.Count)
                    return false;
                foreach (var pair in left.Obj)
                    if (!right.Obj.ContainsKey(pair.Key) || !Equivalent(pair.Value, right[pair.Key]))
                        return false;
                return true;
            }
            if (left.Arr != null || right.Arr != null)
            {
                if (left.Arr == null || right.Arr == null || left.Arr.Count != right.Arr.Count)
                    return false;
                for (int i = 0; i < left.Arr.Count; i++)
                    if (!Equivalent(left.Arr[i], right.Arr[i])) return false;
                return true;
            }
            if (left.Str != null || right.Str != null)
                return left.Str == right.Str;
            if (left.IsBool || right.IsBool)
                return left.IsBool && right.IsBool && left.Bool == right.Bool;
            return left.Num == right.Num;
        }

        public static JVal Merge(JVal baseValue, JVal patch)
        {
            if (patch == null || patch.IsNull) return Clone(patch ?? Null);
            if (patch.Obj == null) return Clone(patch);

            var result = Clone(baseValue ?? Null);
            if (result.Obj == null) result = new JVal { Obj = new Dictionary<string, JVal>() };
            foreach (var pair in patch.Obj)
                result.Obj[pair.Key] = pair.Value.Obj != null
                    ? Merge(result[pair.Key], pair.Value)
                    : Clone(pair.Value);
            return result;
        }

        // Build a patch with the same shape as `shape`, taking leaf values from `source`.
        public static JVal OverlayByShape(JVal shape, JVal source)
        {
            if (shape == null || shape.IsNull) return Null;
            if (shape.Obj == null) return Clone(source ?? Null);
            var result = new JVal { Obj = new Dictionary<string, JVal>() };
            foreach (var pair in shape.Obj)
                result.Obj[pair.Key] = pair.Value.Obj != null
                    ? OverlayByShape(pair.Value, source?[pair.Key])
                    : Clone(source?[pair.Key] ?? Null);
            return result;
        }

        public static JVal Clone(JVal value)
        {
            if (value == null || value.IsNull) return Null;
            if (value.Obj != null)
            {
                var obj = new Dictionary<string, JVal>();
                foreach (var pair in value.Obj) obj[pair.Key] = Clone(pair.Value);
                return new JVal { Obj = obj };
            }
            if (value.Arr != null)
            {
                var arr = new List<JVal>();
                foreach (var item in value.Arr) arr.Add(Clone(item));
                return new JVal { Arr = arr };
            }
            return new JVal
            {
                Str = value.Str,
                Num = value.Num,
                Bool = value.Bool,
                IsNull = value.IsNull,
                IsBool = value.IsBool,
                IsNumber = value.IsNumber,
            };
        }

        public static string ToJson(JVal value)
        {
            if (value == null || value.IsNull) return "null";
            if (value.Obj != null)
            {
                var parts = new List<string>();
                foreach (var pair in value.Obj)
                    parts.Add(Q(pair.Key) + ":" + ToJson(pair.Value));
                return "{" + string.Join(",", parts.ToArray()) + "}";
            }
            if (value.Arr != null)
            {
                var parts = new List<string>();
                foreach (var item in value.Arr) parts.Add(ToJson(item));
                return "[" + string.Join(",", parts.ToArray()) + "]";
            }
            if (value.Str != null) return Q(value.Str);
            if (value.IsBool) return B(value.Bool);
            return value.Num.ToString("R", CultureInfo.InvariantCulture);
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
