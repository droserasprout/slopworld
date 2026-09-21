using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
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
            var message = new JsonParser(JsonParser.Settings.Default.WithIgnoreUnknownFields(true)).Parse<T>(JVal.ToJson(value));
            return parser.ParseFrom(message.ToByteArray());
        }
        public static JVal Json(IMessage value) => JVal.Parse(Token(value).ToString());
        static JToken Token(object value)
        {
            if (value == null) return JValue.CreateNull();
            if (value is IMessage message)
            {
                var result = new JObject();
                foreach (var field in message.Descriptor.Fields.InFieldNumberOrder())
                {
                    if (field.HasPresence && !field.Accessor.HasValue(message)) continue;
                    result[field.Name] = Token(field.Accessor.GetValue(message));
                }
                return result;
            }
            if (value is IDictionary map)
            {
                var result = new JObject();
                foreach (DictionaryEntry entry in map) result[(string)entry.Key] = Token(entry.Value);
                return result;
            }
            if (value is string) return new JValue(value);
            if (value is IEnumerable items) return new JArray(items.Cast<object>().Select(Token));
            return new JValue(value);
        }
        public static string ToJson(this AgentTemplateInfo value, SessionInfo form) => JVal.ToJson(Json(value.ToWire(form)));
        public static string ToJson(this PresetInfo value) => Token(value.ToWire()).ToString(Newtonsoft.Json.Formatting.None);
        public static string ToJson(this CommandInfo value) => Token(value.ToWire()).ToString(Newtonsoft.Json.Formatting.None);
        public static string ToJson(this SessionInfo value) => Token(value.ToWire()).ToString(Newtonsoft.Json.Formatting.None);
        public static string ToJson(this ProjectInfo value) => Token(value.ToWire()).ToString(Newtonsoft.Json.Formatting.None);
        public static string ToJson(this LibraryItemInfo value) => Token(value.ToWire()).ToString(Newtonsoft.Json.Formatting.None);
        public static string ToJson(this DnsConfig value) => Token(value.ToWire()).ToString(Newtonsoft.Json.Formatting.None);
        public static string ToJson(this SessionLimits value) => Token(value.ToWire()).ToString(Newtonsoft.Json.Formatting.None);
        public static string ToPatchJson(this DaemonConfig value, string baseline = null)
        {
            var patch = value.ToPatch(baseline == null ? null : Read<Wire.EditableConfig>(JVal.Parse(baseline)));
            return JVal.ToJson(PatchJson(patch));
        }
        public static JVal PatchJson(Wire.ConfigPatch patch)
        {
            var result = new JObject(); var values = JObject.Parse(JVal.ToJson(Json(patch.Values)));
            foreach (var path in patch.Paths)
            {
                string[] parts = path.Split('.').Select(p => p.Replace("~1", ".").Replace("~0", "~")).ToArray();
                JToken source = values; JObject target = result;
                for (int i = 0; i < parts.Length - 1; i++) { source = source[parts[i]]; if (target[parts[i]] == null) target[parts[i]] = new JObject(); target = (JObject)target[parts[i]]; }
                target[parts.Last()] = source[parts.Last()].DeepClone();
            }
            return JVal.Parse(result.ToString());
        }
        public static void Acknowledge(this DaemonConfigDraft draft, string submitted, Dictionary<string,string> texts) =>
            draft.Acknowledge(Read<Wire.EditableConfig>(JVal.Parse(submitted)), texts);
    }
}
