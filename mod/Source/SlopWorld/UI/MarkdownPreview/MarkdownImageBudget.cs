using System.Collections.Generic;

namespace SlopWorld
{
    // Reserve worst-case decoded space before a request, so simultaneous replies cannot
    // exceed the document budget. The resource store owns textures and viewport eviction.
    sealed class MarkdownImageBudget
    {
        public const long ImageBytes = 16 * 1024 * 1024;
        public const long TotalBytes = 64 * 1024 * 1024;
        const int MaxPending = 2;
        readonly Dictionary<string, long> _bytes = new Dictionary<string, long>();
        readonly HashSet<string> _pending = new HashSet<string>();
        public long UsedBytes { get; private set; }

        public bool TryReserve(string path)
        {
            if (_bytes.ContainsKey(path) || _pending.Count >= MaxPending ||
                UsedBytes + ImageBytes > TotalBytes) return false;
            _bytes.Add(path, ImageBytes);
            _pending.Add(path);
            UsedBytes += ImageBytes;
            return true;
        }

        public bool Complete(string path, long bytes)
        {
            if (!_pending.Contains(path) || bytes <= 0 || bytes > ImageBytes) return false;
            _pending.Remove(path);
            UsedBytes += bytes - _bytes[path];
            _bytes[path] = bytes;
            return true;
        }

        public void Release(string path)
        {
            if (_bytes.TryGetValue(path, out var bytes)) UsedBytes -= bytes;
            _bytes.Remove(path);
            _pending.Remove(path);
        }

        public void Clear()
        {
            _bytes.Clear();
            _pending.Clear();
            UsedBytes = 0;
        }
    }

}
