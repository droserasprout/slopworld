using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
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
    }

    // Apply the same strict wire grammar during library tree loading and streaming skip.
    internal sealed class JsonReader : JsonTextReader
    {
        // Avoid unused per-token source locations.
        static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
        {
            LineInfoHandling = LineInfoHandling.Ignore,
        };
        static readonly Regex NumberSyntax = new Regex(
            @"\G-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?(?=$|[ \t\r\n,}\]])",
            RegexOptions.CultureInvariant);
        internal JsonReader(string text) : base(new StringReader(text ?? ""))
        {
            RejectJsonNetExtensions(text ?? "");
            CloseInput = true;
            DateParseHandling = DateParseHandling.None;
            FloatParseHandling = FloatParseHandling.Double;
            Culture = CultureInfo.InvariantCulture;
            MaxDepth = 128;
        }

        // Reject syntax extensions that Json.NET hides during tokenization.
        static void RejectJsonNetExtensions(string text)
        {
            bool inString = false;
            bool escaped = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escaped) { escaped = false; continue; }
                    if (c == '\\') { escaped = true; continue; }
                    if (c == '"') inString = false;
                    if (c < 0x20) throw new FormatException("Control characters must be escaped.");
                    continue;
                }

                if (c == '"') { inString = true; continue; }
                if (char.IsWhiteSpace(c) && c != ' ' && c != '\t' && c != '\r' && c != '\n')
                    throw new FormatException("Only JSON whitespace is allowed.");
                if (c == '\'' || (c == '/' && i + 1 < text.Length &&
                    (text[i + 1] == '/' || text[i + 1] == '*')))
                    throw new FormatException("JSON extensions are not allowed.");
                if (c == ',')
                {
                    int next = i + 1;
                    while (next < text.Length && (text[next] == ' ' || text[next] == '\t' ||
                        text[next] == '\r' || text[next] == '\n')) next++;
                    if (next < text.Length && (text[next] == ']' || text[next] == '}'))
                        throw new FormatException("Trailing commas are not allowed.");
                }
                if (c == '-' || c == '+' || c == '.' || (c >= '0' && c <= '9'))
                {
                    var number = NumberSyntax.Match(text, i);
                    if (!number.Success) throw new FormatException("Invalid JSON number.");
                    i += number.Length - 1;
                }
            }
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
}
