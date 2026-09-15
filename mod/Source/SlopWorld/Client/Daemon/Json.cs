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
            var reader = new JsonReader(s);
            var value = reader.Value();
            reader.Finish();
            return value;
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
    // One grammar for decoded values and allocation-free skipping of unneeded values.
    // Envelopes use the same reader so malformed payloads cannot replace valid queued frames.
    internal struct JsonReader
    {
        readonly string _text;
        int _at;

        internal JsonReader(string text) { _text = text ?? ""; _at = 0; }

        char Peek()
        {
            while (_at < _text.Length && (_text[_at] == ' ' || _text[_at] == '\t' ||
                _text[_at] == '\r' || _text[_at] == '\n')) _at++;
            return _at < _text.Length ? _text[_at] : '\0';
        }

        internal bool Take(char token)
        {
            if (Peek() != token || _at == _text.Length) return false;
            _at++;
            return true;
        }

        internal void Expect(char token)
        {
            if (!Take(token)) throw Invalid();
        }

        internal void Finish()
        {
            Peek();
            if (_at != _text.Length) throw Invalid();
        }

        // The closing delimiter is legal before the first item or after a value, never
        // immediately after a comma. The next value/key reader rejects that trailing comma.
        internal bool More(ref bool first, char end)
        {
            if (Take(end)) return false;
            if (!first) Expect(',');
            first = false;
            return true;
        }

        internal JVal Value(bool decode = true, int depth = 0)
        {
            if (depth > 128) throw Invalid();
            bool first = true;
            switch (Peek())
            {
                case '{':
                    _at++;
                    var obj = decode ? new Dictionary<string, JVal>() : null;
                    while (More(ref first, '}'))
                    {
                        string key = String(decode);
                        Expect(':');
                        var item = Value(decode, depth + 1);
                        if (decode) obj[key] = item;
                    }
                    return decode ? new JVal { Obj = obj } : null;
                case '[':
                    _at++;
                    var arr = decode ? new List<JVal>() : null;
                    while (More(ref first, ']'))
                    {
                        var item = Value(decode, depth + 1);
                        if (decode) arr.Add(item);
                    }
                    return decode ? new JVal { Arr = arr } : null;
                case '"':
                    string text = String(decode);
                    return decode ? new JVal { Str = text } : null;
                case 't':
                case 'f':
                case 'n':
                    char kind = _text[_at];
                    string literal = kind == 't' ? "true" : kind == 'f' ? "false" : "null";
                    if (_text.Length - _at < literal.Length ||
                        string.CompareOrdinal(_text, _at, literal, 0, literal.Length) != 0)
                        throw Invalid();
                    _at += literal.Length;
                    return !decode ? null : kind == 'n' ? JVal.Null :
                        new JVal { Bool = kind == 't', IsBool = true };
                default:
                    double number = Number(decode);
                    return decode ? new JVal { Num = number, IsNumber = true } : null;
            }
        }

        internal string String(bool decode = true)
        {
            Expect('"');
            int start = _at;
            StringBuilder builder = null;
            while (true)
            {
                // Scan plain spans with a local cursor; decoding and skipping take the same
                // fast path, without a builder check or field write for every character.
                int end = _at;
                while (end < _text.Length && _text[end] >= 0x20 &&
                       _text[end] != '"' && _text[end] != '\\') end++;
                _at = end;
                if (_at == _text.Length) throw Invalid();
                builder?.Append(_text, start, end - start);
                char c = _text[_at++];
                if (c == '"') return !decode ? null : builder == null ?
                    _text.Substring(start, end - start) : builder.ToString();
                if (c < 0x20) throw Invalid();
                if (decode && builder == null)
                {
                    builder = new StringBuilder();
                    builder.Append(_text, start, end - start);
                }
                if (_at == _text.Length) throw Invalid();
                switch (c = _text[_at++])
                {
                    case '"': case '\\': case '/': break;
                    case 'b': c = '\b'; break;
                    case 'f': c = '\f'; break;
                    case 'n': c = '\n'; break;
                    case 'r': c = '\r'; break;
                    case 't': c = '\t'; break;
                    case 'u':
                        if (_text.Length - _at < 4) throw Invalid();
                        int cp = 0;
                        for (int i = 0; i < 4; i++)
                        {
                            char hex = _text[_at++];
                            int digit = hex >= '0' && hex <= '9' ? hex - '0' :
                                hex >= 'a' && hex <= 'f' ? hex - 'a' + 10 :
                                hex >= 'A' && hex <= 'F' ? hex - 'A' + 10 : -1;
                            if (digit < 0) throw Invalid();
                            cp = cp * 16 + digit;
                        }
                        c = (char)cp;
                        break;
                    default: throw Invalid();
                }
                builder?.Append(c);
                start = _at;
            }
        }

        bool Digit() => _at < _text.Length && _text[_at] >= '0' && _text[_at] <= '9';

        void Digits()
        {
            if (!Digit()) throw Invalid();
            while (Digit()) _at++;
        }

        internal double Number(bool decode = true)
        {
            Peek();
            int start = _at;
            if (_at < _text.Length && _text[_at] == '-') _at++;
            if (_at < _text.Length && _text[_at] == '0') _at++;
            else Digits();
            if (_at < _text.Length && _text[_at] == '.') { _at++; Digits(); }
            if (_at < _text.Length && (_text[_at] == 'e' || _text[_at] == 'E'))
            {
                _at++;
                if (_at < _text.Length && (_text[_at] == '+' || _text[_at] == '-')) _at++;
                Digits();
            }
            if (!decode) return 0;
            double.TryParse(_text.Substring(start, _at - start), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var value);
            return value;
        }

        FormatException Invalid() => new FormatException("Invalid JSON at offset " + _at);
    }
}
