using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SlopWorld
{
    // Wire accessors over Json.NET tokens, with missing-value and patch semantics.
    public sealed class JVal
    {
        readonly JToken _token;

        internal JToken Token => _token;

        internal JVal(JToken token) => _token = token ?? JValue.CreateNull();

        public static readonly JVal Null = new JVal(JValue.CreateNull());

        public JVal this[string key]
        {
            get
            {
                var obj = _token as JObject;
                if (obj == null || key == null) return Null;
                return obj.TryGetValue(key, out var value) ? Wrap(value) : Null;
            }
        }

        public JVal this[int index]
        {
            get
            {
                var arr = _token as JArray;
                return arr != null && index >= 0 && index < arr.Count ? Wrap(arr[index]) : Null;
            }
        }

        public int Count => (_token as JArray)?.Count ?? 0;

        public bool IsNull => _token.Type == JTokenType.Null;
        public bool IsObject => _token.Type == JTokenType.Object;
        public bool IsArray => _token.Type == JTokenType.Array;
        public bool IsBool => _token.Type == JTokenType.Boolean;
        public bool IsNumber => _token.Type == JTokenType.Integer ||
            _token.Type == JTokenType.Float;

        public string Str => _token.Type == JTokenType.String ? (string)_token : null;
        public double Num => IsNumber ? Convert.ToDouble(_token.Value<object>(),
            CultureInfo.InvariantCulture) : 0d;
        public bool Bool => IsBool && (bool)_token;

        public string AsString(string fallback = "") => Str ?? fallback;
        public int AsInt(int fallback = 0) => Str == null && !IsNull ? (int)Num : fallback;
        public long AsLong(long fallback = 0) => Str == null && !IsNull ? (long)Num : fallback;
        public bool AsBool(bool fallback = false) => IsNull ? fallback : Bool;
        public float AsFloat(float fallback = 0f) => Str == null && !IsNull ? (float)Num : fallback;

        public IEnumerable<JVal> Items => ItemsOf(_token as JArray);

        public IEnumerable<KeyValuePair<string, JVal>> ObjectItems => ObjectItemsOf(_token as JObject);

        static IEnumerable<JVal> ItemsOf(JArray array)
        {
            if (array == null) yield break;
            foreach (var item in array) yield return Wrap(item);
        }

        static IEnumerable<KeyValuePair<string, JVal>> ObjectItemsOf(JObject obj)
        {
            if (obj == null) yield break;
            foreach (var property in obj.Properties())
                yield return new KeyValuePair<string, JVal>(property.Name, Wrap(property.Value));
        }

        internal static JVal Wrap(JToken token) => token == null || token.Type == JTokenType.Null
            ? Null : new JVal(token);

        // Construction helpers keep editable projections in the token tree. Callers should
        // only turn the finished value into text at the transport boundary.
        internal static JVal ObjectValue() => new JVal(new JObject());
        internal static JVal ArrayValue() => new JVal(new JArray());
        internal static JVal StringValue(string value) => new JVal(new JValue(value ?? ""));
        internal static JVal IntValue(int value) => new JVal(new JValue(value));
        internal static JVal BoolValue(bool value) => new JVal(new JValue(value));

        internal void Put(string key, JVal value)
        {
            var obj = _token as JObject;
            if (obj == null) throw new InvalidOperationException("JSON value is not an object");
            obj[key] = (value ?? Null).Token.DeepClone();
        }

        internal void Add(JVal value)
        {
            var array = _token as JArray;
            if (array == null) throw new InvalidOperationException("JSON value is not an array");
            array.Add((value ?? Null).Token.DeepClone());
        }

        public static JVal Parse(string text)
        {
            try
            {
                using var reader = new JsonReader(text);
                var value = reader.Value();
                reader.Finish();
                return value;
            }
            catch (FormatException) { throw; }
            catch (JsonException exception)
            {
                throw new FormatException(exception.Message, exception);
            }
        }

        public static bool Equivalent(JVal left, JVal right)
        {
            if ((left ?? Null).IsNumber && (right ?? Null).IsNumber)
                return (left ?? Null).Num == (right ?? Null).Num;
            return JToken.DeepEquals((left ?? Null).Token, (right ?? Null).Token);
        }

        // Wire patches replace nulls/arrays and merge objects recursively.
        public static JVal Merge(JVal baseValue, JVal patch)
        {
            if (patch == null || patch.IsNull) return Clone(patch ?? Null);
            if (!patch.IsObject) return Clone(patch);

            var result = Clone(baseValue ?? Null).Token as JObject ?? new JObject();
            foreach (var pair in patch.ObjectItems)
            {
                result[pair.Key] = pair.Value.IsObject
                    ? Merge(Wrap(result[pair.Key]), pair.Value).Token
                    : pair.Value.Token.DeepClone();
            }
            return new JVal(result);
        }

        // Select source leaves using the patch shape.
        public static JVal OverlayByShape(JVal shape, JVal source)
        {
            if (shape == null || shape.IsNull) return Null;
            if (!shape.IsObject) return Clone(source ?? Null);

            var result = new JObject();
            foreach (var pair in shape.ObjectItems)
            {
                result[pair.Key] = pair.Value.IsObject
                    ? OverlayByShape(pair.Value, source?[pair.Key]).Token
                    : (source?[pair.Key] ?? Null).Token.DeepClone();
            }
            return new JVal(result);
        }

        public static JVal Clone(JVal value)
        {
            if (value == null || value.IsNull) return Null;
            return new JVal(value.Token.DeepClone());
        }

        public static string ToJson(JVal value) => (value ?? Null).Token.ToString(Formatting.None);

        public static string Q(string value) => JsonConvert.ToString(value ?? "");

        public static string B(bool value) => value ? "true" : "false";

        // Screen ingestion reads a large string array on every live frame. Returning the
        // string directly avoids manufacturing a short-lived JVal wrapper for each row.
        internal string StringAt(int index, string fallback = "")
        {
            var array = _token as JArray;
            if (array == null || index < 0 || index >= array.Count) return fallback;
            var value = array[index];
            return value != null && value.Type == JTokenType.String ? (string)value : fallback;
        }
    }

    // Json.NET reader used for retained library trees and full message decoding.
    internal sealed class JsonReader : JsonTextReader
    {
        // Avoid unused per-token source locations.
        static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
        {
            LineInfoHandling = LineInfoHandling.Ignore,
        };
        internal JsonReader(string text) : base(new StringReader(text ?? ""))
        {
            // Json.NET supplies the retained value tree, while the scanner below supplies the
            // strict grammar that Json.NET's token stream does not expose (notably trailing
            // commas, comments and non-JSON whitespace).
            JsonScanner.Validate(text ?? "");
            CloseInput = true;
            DateParseHandling = DateParseHandling.None;
            FloatParseHandling = FloatParseHandling.Double;
            Culture = CultureInfo.InvariantCulture;
            MaxDepth = 128;
        }

        internal void Expect(JsonToken token)
        {
            ReadRequired();
            if (TokenType != token) throw Invalid();
        }

        internal bool More(JsonToken end)
        {
            ReadRequired();
            if (TokenType == end) return false;
            if (TokenType != JsonToken.PropertyName) throw Invalid();
            return true;
        }

        internal string PropertyName()
        {
            if (TokenType != JsonToken.PropertyName) throw Invalid();
            return (string)base.Value;
        }

        internal string String()
        {
            ReadRequired();
            if (TokenType != JsonToken.String) throw Invalid();
            return (string)base.Value;
        }

        internal double Number()
        {
            ReadRequired();
            if (TokenType != JsonToken.Integer && TokenType != JsonToken.Float)
                throw Invalid();
            return NumberValue();
        }

        internal new JVal Value(bool decode = true)
        {
            ReadRequired();
            if (decode) return JVal.Wrap(JToken.ReadFrom(this, LoadSettings));
            if (TokenType == JsonToken.StartObject || TokenType == JsonToken.StartArray)
            {
                Skip();
                if (TokenType != JsonToken.EndObject && TokenType != JsonToken.EndArray)
                    throw Invalid();
            }
            return null;
        }

        internal void Finish()
        {
            if (Read()) throw Invalid();
        }

        double NumberValue()
        {
            double value;
            try { value = Convert.ToDouble(base.Value, CultureInfo.InvariantCulture); }
            catch (Exception exception) when (exception is FormatException ||
                                               exception is InvalidCastException ||
                                               exception is OverflowException)
            {
                throw Invalid(exception.Message);
            }
            if (double.IsNaN(value) || double.IsInfinity(value)) throw Invalid();
            return value;
        }

        public override bool Read()
        {
            bool hasToken;
            try
            {
                hasToken = base.Read();
            }
            catch (JsonException exception) { throw Invalid(exception.Message); }
            if (!hasToken) return false;
            switch (TokenType)
            {
                case JsonToken.Comment:
                case JsonToken.Undefined:
                case JsonToken.Raw:
                case JsonToken.StartConstructor:
                    throw Invalid();
                case JsonToken.PropertyName:
                    if (QuoteChar != '"') throw Invalid();
                    break;
                case JsonToken.Integer:
                case JsonToken.Float:
                    // Preserve double-based conversions and equality.
                    SetToken(JsonToken.Float, NumberValue());
                    break;
            }
            return true;
        }

        void ReadRequired()
        {
            if (!Read()) throw Invalid();
        }

        FormatException Invalid(string message = null) =>
            new FormatException(message ?? "Invalid JSON at token " + TokenType);
    }

    // A strict structural pass used for envelope inspection and skipped values. Json.NET
    // necessarily materializes every string token even when a live frame will be coalesced
    // away; this scanner validates the same JSON grammar without decoding those discarded
    // strings. Retained values still go through JsonReader/JToken.ReadFrom below.
    internal struct JsonScanner
    {
        readonly string _text;
        int _at;

        internal JsonScanner(string text)
        {
            _text = text ?? "";
            _at = 0;
        }

        internal static void Validate(string text)
        {
            var reader = new JsonScanner(text);
            reader.Value();
            reader.Finish();
        }

        char Peek()
        {
            while (_at < _text.Length && (_text[_at] == ' ' || _text[_at] == '\t' ||
                _text[_at] == '\r' || _text[_at] == '\n')) _at++;
            return _at < _text.Length ? _text[_at] : '\0';
        }

        bool Take(char token)
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

        internal bool More(ref bool first, char end)
        {
            if (Take(end)) return false;
            if (!first) Expect(',');
            first = false;
            return true;
        }

        internal void Value(bool decode = false, int depth = 0)
        {
            if (depth > 128) throw Invalid();
            bool first = true;
            switch (Peek())
            {
                case '{':
                    _at++;
                    while (More(ref first, '}'))
                    {
                        String(false);
                        Expect(':');
                        Value(false, depth + 1);
                    }
                    return;
                case '[':
                    _at++;
                    while (More(ref first, ']')) Value(false, depth + 1);
                    return;
                case '"':
                    String(decode);
                    return;
                case 't':
                    Literal("true");
                    return;
                case 'f':
                    Literal("false");
                    return;
                case 'n':
                    Literal("null");
                    return;
                default:
                    Number();
                    return;
            }
        }

        internal string String(bool decode = true)
        {
            Expect('"');
            int start = _at;
            StringBuilder builder = null;
            while (true)
            {
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

        void Literal(string literal)
        {
            if (_text.Length - _at < literal.Length ||
                string.CompareOrdinal(_text, _at, literal, 0, literal.Length) != 0)
                throw Invalid();
            _at += literal.Length;
        }

        bool Digit() => _at < _text.Length && _text[_at] >= '0' && _text[_at] <= '9';

        void Digits()
        {
            if (!Digit()) throw Invalid();
            while (Digit()) _at++;
        }

        void Number()
        {
            Peek();
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
        }

        internal bool NumberIsZero()
        {
            Peek();
            int start = _at;
            Number();
            int end = _at;
            bool needsConversion = false;
            bool nonzero = false;
            for (int i = start; i < end; i++)
            {
                char c = _text[i];
                if (c == '.' || c == 'e' || c == 'E') needsConversion = true;
                else if (c >= '1' && c <= '9') nonzero = true;
            }
            if (!needsConversion) return !nonzero;
            if (!double.TryParse(_text.Substring(start, end - start), NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out var value) ||
                double.IsNaN(value) || double.IsInfinity(value)) throw Invalid();
            return value == 0d;
        }

        FormatException Invalid() => new FormatException("Invalid JSON at offset " + _at);
    }
}
