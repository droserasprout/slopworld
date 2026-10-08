using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Newtonsoft.Json.Linq;
namespace SlopWorld
{
    // Human-readable test fixtures only. Exercise the generated binary codec before delivery.
    static class ProtobufFixtures
    {
        public static T Read<T>(JVal value) where T : IMessage<T>, new()
        {
            if (value == null || value.IsNull) return default(T);
            var parser = new MessageParser<T>(() => new T());
            var token = value.Token.DeepClone();
            PreserveIntegerTokens(token, new T().Descriptor);
            var message = JsonParser.Default.Parse<T>(token.ToString(Newtonsoft.Json.Formatting.None));
            return parser.ParseFrom(message.ToByteArray());
        }
        // JSON fixtures accept bare exact integers; Protobuf JSON requires strings for
        // lossless 64-bit parsing. Walk only schema-owned fields and keep unknown-field rejection.
        static void PreserveIntegerTokens(JToken token, MessageDescriptor descriptor)
        {
            if (!(token is JObject obj)) return;
            foreach (var field in descriptor.Fields.InFieldNumberOrder())
            {
                var value = obj[field.Name] ?? obj[field.JsonName];
                if (value == null || value.Type == JTokenType.Null) continue;
                if (field.IsMap)
                {
                    if (!(value is JObject map)) continue;
                    var mapValue = field.MessageType.FindFieldByNumber(2);
                    foreach (var entry in map.Properties().ToArray()) PreserveFieldInteger(entry.Value, mapValue);
                }
                else if (field.IsRepeated)
                {
                    if (value is JArray array)
                        foreach (var item in array.ToArray()) PreserveFieldInteger(item, field);
                }
                else PreserveFieldInteger(value, field);
            }
        }

        static void PreserveFieldInteger(JToken token, FieldDescriptor field)
        {
            if (field.FieldType == FieldType.Message)
                PreserveIntegerTokens(token, field.MessageType);
            else if (token.Type == JTokenType.Integer && (field.FieldType == FieldType.Int64 ||
                field.FieldType == FieldType.SInt64 || field.FieldType == FieldType.SFixed64 ||
                field.FieldType == FieldType.UInt64 || field.FieldType == FieldType.Fixed64))
                token.Replace(new JValue(token.ToString(Newtonsoft.Json.Formatting.None)));
        }

        public static JVal Json(IMessage value) => JVal.Parse(ToJsonToken(value).ToString());
        static JToken ToJsonToken(object value)
        {
            if (value == null) return JValue.CreateNull();
            if (value is IMessage message)
            {
                var result = new JObject();
                foreach (var field in message.Descriptor.Fields.InFieldNumberOrder())
                {
                    if (field.HasPresence && !field.Accessor.HasValue(message)) continue;
                    result[field.Name] = ToJsonToken(field.Accessor.GetValue(message));
                }
                return result;
            }
            if (value is IDictionary map)
            {
                var result = new JObject();
                foreach (DictionaryEntry entry in map) result[(string)entry.Key] = ToJsonToken(entry.Value);
                return result;
            }
            if (value is string) return new JValue(value);
            if (value is IEnumerable items) return new JArray(items.Cast<object>().Select(ToJsonToken));
            return new JValue(value);
        }
        public static string ToJson(this AgentTemplateInfo value, SessionInfo form) => JVal.ToJson(Json(value.ToWire(form)));
        public static string ToJson(this PresetInfo value) => ToJsonToken(value.ToWire()).ToString(Newtonsoft.Json.Formatting.None);
        public static string ToJson(this CommandInfo value) => ToJsonToken(value.ToWire()).ToString(Newtonsoft.Json.Formatting.None);
        public static string ToJson(this SessionInfo value) => ToJsonToken(value.ToWire()).ToString(Newtonsoft.Json.Formatting.None);
        public static string ToJson(this ProjectInfo value) => ToJsonToken(value.ToWire()).ToString(Newtonsoft.Json.Formatting.None);
        public static string ToJson(this LibraryItemInfo value) => ToJsonToken(value.ToWire()).ToString(Newtonsoft.Json.Formatting.None);
        public static string ToJson(this DnsConfig value) => ToJsonToken(value.ToWire()).ToString(Newtonsoft.Json.Formatting.None);
        public static string ToJson(this SessionLimits value) => ToJsonToken(value.ToWire()).ToString(Newtonsoft.Json.Formatting.None);
        public static string ToPatchJson(this DaemonConfig value, string baseline = null)
        {
            var patch = value.ToPatch(baseline == null ? null : Read<Wire.EditableConfig>(JVal.Parse(baseline)));
            return JVal.ToJson(PatchJson(patch));
        }
        public static JVal PatchJson(Wire.ConfigPatch patch)
        {
            var result = new JObject();
            var values = (JObject)ToJsonToken(patch.Values);
            foreach (var path in patch.Paths) InsertPatchPath(result, values, DecodePatchPath(path));
            return JVal.Parse(result.ToString());
        }

        static string[] DecodePatchPath(string path) =>
            path.Split('.').Select(part => part.Replace("~1", ".").Replace("~0", "~")).ToArray();

        static void InsertPatchPath(JObject result, JObject values, string[] parts)
        {
            JToken source = values;
            JObject target = result;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                source = source[parts[i]];
                if (target[parts[i]] == null) target[parts[i]] = new JObject();
                target = (JObject)target[parts[i]];
            }
            target[parts.Last()] = source[parts.Last()].DeepClone();
        }
        public static void Acknowledge(this DaemonConfigDraft draft, string submitted, Dictionary<string, string> texts) =>
            draft.Acknowledge(Read<Wire.EditableConfig>(JVal.Parse(submitted)), texts);
    }
}
