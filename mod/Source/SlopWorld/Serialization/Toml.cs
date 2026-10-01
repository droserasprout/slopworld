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
    // Tomlyn handles TOML grammar and diagnostics.
    // The settings adapter accepts only top-level scalar entries.
    // Keep their TOML types until each settings field converts them, preventing accidental conversion of tables or arrays to strings.
    // ParseFlat preserves the existing text API for endpoint and jukebox readers.
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

                // Reject tables, arrays of tables, and arrays from the flat application schema.
                // Tomlyn supports these types, but this adapter does not.
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
                        return ((IConvertible)_value).ToString(CultureInfo.InvariantCulture);
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

        // Preserve compatibility with endpoint and likes readers.
        // Format values from their parsed TOML types instead of copying raw text.
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

        // Structured catalogs use separate schema readers that accept tables and arrays.
        // Keep this API separate to preserve the flat settings format.
        internal static TomlynTable ParseTable(string text) => ParseModel(text);

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
