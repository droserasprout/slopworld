using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Verse;

namespace SlopWorld
{
    // The mod has no TOML dependency of its own: the daemon's parser cannot be loaded by
    // RimWorld, and putting a second general-purpose parser in the shipped Assemblies folder
    // would make the mod's install fragile. This is the small TOML surface the jukebox needs:
    // strings, integers, arrays, [metadata], and [[stream]].
    internal sealed class JukeboxDefinition
    {
        public string Id;
        public string Name;
        public string Donate;
        public int DefaultRate;
        public readonly List<JukeboxStream> Streams = new List<JukeboxStream>();
    }

    internal sealed class JukeboxStream
    {
        public int Rate;
        public string Key;
        public string Url;
    }

    internal static class JukeboxConfig
    {
        public static List<JukeboxDefinition> Load()
        {
            var definitions = new List<JukeboxDefinition>();
            ReadDirectory(BuiltinDirectory(), definitions, true);
            ReadDirectory(UserDirectory(), definitions, false);
            return definitions;
        }

        public static string UserDirectory()
        {
            string root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(root))
                root = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(root, "slopworld", "jukebox");
        }

        static string BuiltinDirectory()
        {
            string root = SlopWorldMod.Instance?.Content?.RootDir;
            return Path.Combine(root ?? "", "Jukebox");
        }

        static void ReadDirectory(string directory, List<JukeboxDefinition> definitions,
            bool builtin)
        {
            string[] paths;
            try
            {
                if (!Directory.Exists(directory)) return;
                paths = Directory.GetFiles(directory, "*.toml", SearchOption.TopDirectoryOnly);
            }
            catch (Exception e)
            {
                Log.Warning("[SlopWorld] jukebox: could not list " + directory + ": " + e);
                return;
            }

            Array.Sort(paths, StringComparer.Ordinal);
            foreach (string path in paths)
            {
                try
                {
                    var definition = Parse(File.ReadAllText(path), path);
                    int old = definitions.FindIndex(d => d.Id == definition.Id);
                    if (old >= 0) definitions[old] = definition;
                    else definitions.Add(definition);
                }
                catch (Exception e)
                {
                    string kind = builtin ? "builtin" : "user";
                    Log.Warning("[SlopWorld] jukebox: " + kind + " definition "
                        + path + " was ignored: " + e.Message);
                }
            }
        }

        static JukeboxDefinition Parse(string text, string path)
        {
            var document = TomlDocument.Parse(text);
            string id = document.Root.String("id", null);
            if (string.IsNullOrEmpty(id))
                id = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(id)) throw new FormatException("missing id");

            TomlTable metadata = document.Table("metadata");
            string name = metadata?.String("name", null)
                ?? document.Root.String("name", null) ?? id;
            string donate = metadata?.String("donate", "")
                ?? document.Root.String("donate", "") ?? "";

            var definition = new JukeboxDefinition
            {
                Id = id,
                Name = name,
                Donate = donate,
                DefaultRate = document.Root.Int("default_rate", 0),
            };

            foreach (TomlTable table in document.Streams)
                AddStream(definition, table, document.Root);

            // This compact form keeps older hand-written definitions useful too:
            // host/path/rates at the root expands to one stream per rate.
            if (definition.Streams.Count == 0)
                AddLegacyStreams(definition, document.Root);

            if (definition.Streams.Count == 0)
                throw new FormatException("no [[stream]] entries");

            if (definition.DefaultRate == 0)
                definition.DefaultRate = definition.Streams[0].Rate;
            if (!HasRate(definition, definition.DefaultRate))
                throw new FormatException("default_rate has no matching stream");
            return definition;
        }

        static void AddStream(JukeboxDefinition definition, TomlTable table, TomlTable root)
        {
            int rate = table.Int("rate", 0);
            if (rate <= 0) throw new FormatException("stream rate must be positive");

            string url = table.String("url", null);
            if (string.IsNullOrEmpty(url))
            {
                string host = table.String("host", null) ?? root.String("host", "");
                string path = table.String("path", null) ?? root.String("path", "");
                url = host + path.Replace("{rate}", rate.ToString(CultureInfo.InvariantCulture));
            }
            if (!IsHttpUrl(url)) throw new FormatException("stream url is not HTTP(S)");
            if (HasRate(definition, rate))
                throw new FormatException("duplicate stream rate " + rate);

            string key = table.String("key", null) ?? table.String("path", null);
            if (string.IsNullOrEmpty(key))
                key = rate.ToString(CultureInfo.InvariantCulture);
            definition.Streams.Add(new JukeboxStream { Rate = rate, Key = key, Url = url });
        }

        static void AddLegacyStreams(JukeboxDefinition definition, TomlTable root)
        {
            string host = root.String("host", "");
            string path = root.String("path", null);
            IList<object> rates = root.Array("rates");
            if (string.IsNullOrEmpty(path) || rates == null) return;

            foreach (object value in rates)
            {
                if (!(value is int)) throw new FormatException("rates must be integers");
                int rate = (int)value;
                AddStream(definition, new TomlTable
                {
                    ["rate"] = rate,
                    ["path"] = path.Replace("{rate}", rate.ToString(CultureInfo.InvariantCulture)),
                    ["url"] = host + path.Replace("{rate}",
                        rate.ToString(CultureInfo.InvariantCulture)),
                }, root);
            }
        }

        static bool HasRate(JukeboxDefinition definition, int rate)
        {
            foreach (var stream in definition.Streams)
                if (stream.Rate == rate) return true;
            return false;
        }

        static bool IsHttpUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        sealed class TomlDocument
        {
            public readonly TomlTable Root = new TomlTable();
            readonly TomlTable _metadata = new TomlTable();
            public readonly List<TomlTable> Streams = new List<TomlTable>();
            bool _hasMetadata;

            public TomlTable Table(string name)
            {
                return name == "metadata" && _hasMetadata ? _metadata : null;
            }

            public static TomlDocument Parse(string text)
            {
                var result = new TomlDocument();
                TomlTable current = result.Root;
                string[] lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = StripComment(lines[i]).Trim();
                    if (line.Length == 0) continue;

                    if (line.StartsWith("[[", StringComparison.Ordinal))
                    {
                        if (!line.EndsWith("]]", StringComparison.Ordinal))
                            throw new FormatException("line " + (i + 1) + ": bad array table");
                        string name = line.Substring(2, line.Length - 4).Trim();
                        if (name != "stream" && name != "streams")
                            throw new FormatException("line " + (i + 1)
                                + ": unknown array table " + name);
                        current = new TomlTable();
                        result.Streams.Add(current);
                        continue;
                    }

                    if (line[0] == '[')
                    {
                        if (!line.EndsWith("]", StringComparison.Ordinal)
                            || line.IndexOf(']') != line.Length - 1)
                            throw new FormatException("line " + (i + 1) + ": bad table");
                        string name = line.Substring(1, line.Length - 2).Trim();
                        if (name == "metadata")
                        {
                            current = result._metadata;
                            result._hasMetadata = true;
                        }
                        else if (name == "stream" || name == "streams")
                        {
                            current = new TomlTable();
                            result.Streams.Add(current);
                        }
                        else
                        {
                            throw new FormatException("line " + (i + 1)
                                + ": unknown table " + name);
                        }
                        continue;
                    }

                    int equals = FindEquals(line);
                    if (equals < 1)
                        throw new FormatException("line " + (i + 1) + ": expected key = value");
                    string key = line.Substring(0, equals).Trim();
                    if (key.Length == 0) throw new FormatException("line " + (i + 1) + ": empty key");
                    current.Add(key, ParseValue(line.Substring(equals + 1).Trim(), i + 1));
                }
                return result;
            }
        }

        sealed class TomlTable : Dictionary<string, object>
        {
            public new void Add(string key, object value)
            {
                if (ContainsKey(key)) throw new FormatException("duplicate key " + key);
                base.Add(key, value);
            }

            public string String(string key, string fallback)
            {
                object value;
                if (!TryGetValue(key, out value)) return fallback;
                if (value is string) return (string)value;
                throw new FormatException(key + " must be a string");
            }

            public int Int(string key, int fallback)
            {
                object value;
                if (!TryGetValue(key, out value)) return fallback;
                if (value is int) return (int)value;
                throw new FormatException(key + " must be an integer");
            }

            public IList<object> Array(string key)
            {
                object value;
                if (!TryGetValue(key, out value)) return null;
                var array = value as IList<object>;
                if (array == null) throw new FormatException(key + " must be an array");
                return array;
            }
        }

        static object ParseValue(string value, int line)
        {
            if (value.Length == 0) throw new FormatException("line " + line + ": empty value");
            if ((value[0] == '"' && value[value.Length - 1] == '"')
                || (value[0] == '\'' && value[value.Length - 1] == '\''))
                return ParseString(value, line);
            if (value[0] == '[' && value[value.Length - 1] == ']')
            {
                var array = new List<object>();
                foreach (string part in SplitArray(value.Substring(1, value.Length - 2), line))
                    array.Add(ParseValue(part, line));
                return array;
            }
            int integer;
            if (int.TryParse(value.Replace("_", ""), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out integer)) return integer;
            if (value == "true") return true;
            if (value == "false") return false;
            throw new FormatException("line " + line + ": unsupported TOML value");
        }

        static string ParseString(string value, int line)
        {
            char quote = value[0];
            if (value.Length < 2 || value[value.Length - 1] != quote)
                throw new FormatException("line " + line + ": unterminated string");
            string body = value.Substring(1, value.Length - 2);
            if (quote == '\'') return body;

            var result = new StringBuilder();
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c != '\\')
                {
                    result.Append(c);
                    continue;
                }
                if (++i >= body.Length) throw new FormatException("line " + line + ": bad escape");
                c = body[i];
                switch (c)
                {
                    case 'b': result.Append('\b'); break;
                    case 't': result.Append('\t'); break;
                    case 'n': result.Append('\n'); break;
                    case 'f': result.Append('\f'); break;
                    case 'r': result.Append('\r'); break;
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case 'u': result.AppendCodePoint(body, ref i, 4, line); break;
                    case 'U': result.AppendCodePoint(body, ref i, 8, line); break;
                    default: throw new FormatException("line " + line + ": bad escape");
                }
            }
            return result.ToString();
        }

        static List<string> SplitArray(string value, int line)
        {
            var parts = new List<string>();
            int start = 0;
            char quote = '\0';
            int brackets = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (quote != '\0')
                {
                    if (c == quote && (quote == '\'' || i == 0 || value[i - 1] != '\\'))
                        quote = '\0';
                    continue;
                }
                if (c == '\'' || c == '"') { quote = c; continue; }
                if (c == '[') { brackets++; continue; }
                if (c == ']') { brackets--; continue; }
                if (c == ',' && brackets == 0)
                {
                    string part = value.Substring(start, i - start).Trim();
                    if (part.Length == 0) throw new FormatException("line " + line + ": empty array item");
                    parts.Add(part);
                    start = i + 1;
                }
            }
            string last = value.Substring(start).Trim();
            if (last.Length > 0) parts.Add(last);
            return parts;
        }

        static int FindEquals(string line)
        {
            char quote = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quote != '\0')
                {
                    if (c == quote && (quote == '\'' || i == 0 || line[i - 1] != '\\'))
                        quote = '\0';
                }
                else if (c == '\'' || c == '"') quote = c;
                else if (c == '=') return i;
            }
            return -1;
        }

        static string StripComment(string line)
        {
            char quote = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quote != '\0')
                {
                    if (c == quote && (quote == '\'' || i == 0 || line[i - 1] != '\\'))
                        quote = '\0';
                }
                else if (c == '\'' || c == '"') quote = c;
                else if (c == '#') return line.Substring(0, i);
            }
            return line;
        }
    }

    static class StringBuilderExtensions
    {
        public static void AppendCodePoint(this StringBuilder builder, string value, ref int index,
            int digits, int line)
        {
            if (index + digits >= value.Length)
                throw new FormatException("line " + line + ": short Unicode escape");
            int codePoint;
            if (!int.TryParse(value.Substring(index + 1, digits), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out codePoint))
                throw new FormatException("line " + line + ": bad Unicode escape");
            index += digits;
            try
            {
                builder.Append(char.ConvertFromUtf32(codePoint));
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new FormatException("line " + line + ": bad Unicode code point");
            }
        }
    }
}
