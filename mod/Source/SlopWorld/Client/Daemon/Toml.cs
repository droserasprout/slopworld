using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TomlynDateTime = global::Tomlyn.TomlDateTime;
using TomlynDocument = global::Tomlyn.Syntax.DocumentSyntax;
using TomlynTable = global::Tomlyn.Model.TomlTable;
using TomlynToml = global::Tomlyn.Toml;

namespace SlopWorld
{
    // Tomlyn owns TOML grammar and diagnostics. The mod's consumers intentionally use a
    // smaller adapter: only top-level scalar entries are accepted, and their TOML types are
    // retained until each settings field converts them. This prevents a table or array from
    // becoming an accidental string, while ParseFlat keeps the old text-facing API for the
    // endpoint and jukebox readers.
    public static class Toml
    {
        internal enum ScalarType
        {
            String,
            Boolean,
            Integer,
            Float,
            DateTime,
        }

        internal sealed class Scalar
        {
            readonly ScalarType _type;
            readonly object _value;

            Scalar(ScalarType type, object value)
            {
                _type = type;
                _value = value;
            }

            internal static Scalar From(string key, object value)
            {
                if (value is string) return new Scalar(ScalarType.String, value);
                if (value is bool) return new Scalar(ScalarType.Boolean, value);
                if (value is long) return new Scalar(ScalarType.Integer, value);
                if (value is double) return new Scalar(ScalarType.Float, value);
                if (value is TomlynDateTime) return new Scalar(ScalarType.DateTime, value);

                // Tables, table arrays and arrays are deliberately not part of the flat
                // application schema, even though Tomlyn can represent all of them.
                throw new FormatException("TOML flat scalar schema rejects key '" + key +
                                          "': tables and arrays are not supported");
            }

            internal string ToInvariantText()
            {
                switch (_type)
                {
                    case ScalarType.String: return (string)_value;
                    case ScalarType.Boolean:
                        return ((bool)_value) ? "true" : "false";
                    case ScalarType.Integer:
                        return ((long)_value).ToString(CultureInfo.InvariantCulture);
                    case ScalarType.Float:
                        return ((double)_value).ToString("R", CultureInfo.InvariantCulture);
                    case ScalarType.DateTime:
                        return _value.ToString();
                    default: throw new InvalidOperationException("unknown TOML scalar type");
                }
            }

            internal bool TryGetString(out string value)
            {
                if (_type == ScalarType.String)
                {
                    value = (string)_value;
                    return true;
                }
                value = null;
                return false;
            }

            internal bool TryGetBoolean(out bool value)
            {
                if (_type == ScalarType.Boolean)
                {
                    value = (bool)_value;
                    return true;
                }
                value = false;
                return false;
            }

            internal bool TryGetInteger(out int value)
            {
                if (_type == ScalarType.Integer)
                {
                    long number = (long)_value;
                    if (number >= int.MinValue && number <= int.MaxValue)
                    {
                        value = (int)number;
                        return true;
                    }
                }
                value = 0;
                return false;
            }

            internal bool TryGetFloat(out float value)
            {
                double number;
                if (_type == ScalarType.Integer) number = (long)_value;
                else if (_type == ScalarType.Float) number = (double)_value;
                else
                {
                    value = 0f;
                    return false;
                }

                value = (float)number;
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }
        }

        // Compatibility facade for the existing endpoint and likes readers. Values are
        // canonicalized from their parsed TOML types rather than copied from raw text.
        public static Dictionary<string, string> ParseFlat(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in ParseFlatScalars(text))
                values.Add(pair.Key, pair.Value.ToInvariantText());
            return values;
        }

        internal static Dictionary<string, Scalar> ParseFlatScalars(string text)
        {
            var values = new Dictionary<string, Scalar>(StringComparer.Ordinal);
            foreach (var pair in ParseModel(text))
                values.Add(pair.Key, Scalar.From(pair.Key, pair.Value));
            return values;
        }

        public static string Quote(string value)
        {
            string document;
            try
            {
                document = TomlynToml.FromModel(new Dictionary<string, string>
                {
                    ["value"] = value ?? "",
                });
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("could not quote TOML string", exception);
            }

            const string prefix = "value = ";
            if (!document.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Tomlyn did not produce a scalar TOML value");
            return document.Substring(prefix.Length).TrimEnd('\r', '\n');
        }

        static TomlynTable ParseModel(string text)
        {
            TomlynDocument document;
            try
            {
                document = TomlynToml.Parse(text ?? "");
            }
            catch (Exception exception)
            {
                throw new FormatException("TOML parser failed: " + exception.Message, exception);
            }

            if (document.HasErrors)
                throw new FormatException("TOML parser rejected input: " + Diagnostics(document));

            try
            {
                return TomlynToml.ToModel(document);
            }
            catch (Exception exception)
            {
                throw new FormatException("TOML model conversion failed: " + exception.Message,
                                           exception);
            }
        }

        static string Diagnostics(TomlynDocument document)
        {
            var text = new StringBuilder();
            foreach (var diagnostic in document.Diagnostics)
            {
                if (text.Length > 0) text.Append("; ");
                text.Append(diagnostic.ToString());
            }
            return text.Length == 0 ? "unknown parser error" : text.ToString();
        }
    }
}
