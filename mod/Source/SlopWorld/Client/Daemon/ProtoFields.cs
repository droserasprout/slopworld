using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Reflection;
namespace SlopWorld
{
    // Editor-only leaf snapshots. Generated messages own the schema and binary codec.
    internal static class ProtoFields
    {
        public static string Escape(string key) => key.Replace("~", "~0").Replace(".", "~1");
        static string Unescape(string key) => key.Replace("~1", ".").Replace("~0", "~");
        public static Dictionary<string, object> Leaves(IMessage message)
        {
            var result = new Dictionary<string, object>();
            if (message != null) Visit(message, "", result);
            return result;
        }
        static void Visit(IMessage message, string prefix, Dictionary<string, object> result)
        {
            foreach (var field in message.Descriptor.Fields.InFieldNumberOrder())
            {
                string path = prefix + field.Name;
                if (path == "daemon.token" || path == "daemon.bind") continue;
                object value = field.Accessor.GetValue(message);
                if (field.IsMap)
                {
                    foreach (DictionaryEntry entry in (IDictionary)value)
                    {
                        string key = path + "." + Escape((string)entry.Key);
                        if (entry.Value is IMessage nested) Visit(nested, key + ".", result);
                        else result[key] = entry.Value;
                    }
                }
                else if (field.IsRepeated) result[path] = ((IEnumerable)value).Cast<object>().ToArray();
                else if (value is IMessage nested) Visit(nested, path + ".", result);
                else result[path] = field.HasPresence && !field.Accessor.HasValue(message) ? null : value;
            }
        }
        public static bool Equal(object a, object b) => a is object[] aa && b is object[] bb
            ? aa.SequenceEqual(bb) : Equals(a, b);
        public static object ValueAt(IMessage message, string path) => Leaves(message).TryGetValue(path, out var v) ? v : null;
        public static Dictionary<string, object> Changes(IMessage current, IMessage baseline)
        {
            var before = Leaves(baseline);
            return Leaves(current).Where(p => !before.TryGetValue(p.Key, out var old) || !Equal(p.Value, old))
                .ToDictionary(p => p.Key, p => p.Value);
        }
        public static Wire.EditableConfig Apply(Wire.EditableConfig source, IDictionary<string, object> changes)
        {
            var result = source.Clone();
            foreach (var pair in changes) Set(result, pair.Key.Split('.'), 0, pair.Value);
            return result;
        }
        static void Set(IMessage message, string[] parts, int at, object value)
        {
            var field = message.Descriptor.FindFieldByName(parts[at]);
            if (field == null) throw new ArgumentException("Unknown editable field: " + parts[at]);
            if (field.IsMap)
            {
                var map = (IDictionary)field.Accessor.GetValue(message);
                string key = Unescape(parts[at + 1]);
                if (at + 2 == parts.Length) { map[key] = value; return; }
                if (!map.Contains(key)) map[key] = Activator.CreateInstance(field.MessageType.FindFieldByNumber(2).MessageType.ClrType);
                Set((IMessage)map[key], parts, at + 2, value);
            }
            else if (at + 1 < parts.Length)
            {
                var nested = (IMessage)field.Accessor.GetValue(message);
                if (nested == null) { nested = (IMessage)Activator.CreateInstance(field.MessageType.ClrType); field.Accessor.SetValue(message, nested); }
                Set(nested, parts, at + 1, value);
            }
            else if (field.IsRepeated)
            {
                var list = (IList)field.Accessor.GetValue(message);
                list.Clear(); foreach (object item in (object[])value) list.Add(item);
            }
            else if (value == null) field.Accessor.Clear(message);
            else field.Accessor.SetValue(message, value);
        }
    }
}
