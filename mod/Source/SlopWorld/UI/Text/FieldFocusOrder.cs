using System.Collections.Generic;

namespace SlopWorld
{
    // A form publishes its currently enabled fields on each IMGUI pass.
    // Remember names, not control IDs: IDs may change when conditional rows appear.
    public sealed class FieldFocusOrder
    {
        readonly List<string> _fields = new List<string>();
        public string Remembered { get; private set; }
        public int Count => _fields.Count;
        public void Begin() => _fields.Clear();
        public void Register(string name)
        {
            if (!string.IsNullOrEmpty(name) && !_fields.Contains(name)) _fields.Add(name);
        }
        public void Remember(string name)
        {
            if (_fields.Contains(name)) Remembered = name;
        }
        public string Restore() => _fields.Contains(Remembered) ? Remembered : null;
    }
}
