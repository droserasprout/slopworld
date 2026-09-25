using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Generated keys identify artwork, not display widths. Longest matching keys allow
    // a baked sequence to replace its individual components without Unicode range rules.
    public sealed class TextSpriteCatalog
    {
        sealed class Node
        {
            public readonly Dictionary<char, Node> Next = new Dictionary<char, Node>();
            public int Slot = -1;
        }

        readonly Node _root = new Node();
        // The terminal's common printable-ASCII rows can skip trie lookups when no
        // catalog key can begin with one of those characters.
        internal readonly bool HasPrintableAsciiPrefix;
        public static readonly TextSpriteCatalog Shared = new TextSpriteCatalog(TextSpriteData.Keys);

        public TextSpriteCatalog(IEnumerable<string> keys)
        {
            int slot = 0;
            foreach (string key in keys)
            {
                if (string.IsNullOrEmpty(key)) throw new ArgumentException("Empty sprite key");
                if (key[0] >= ' ' && key[0] <= '~') HasPrintableAsciiPrefix = true;
                var node = _root;
                foreach (char c in key)
                {
                    if (!node.Next.TryGetValue(c, out var next)) node.Next[c] = next = new Node();
                    node = next;
                }
                if (node.Slot >= 0) throw new ArgumentException("Duplicate sprite key");
                node.Slot = slot++;
            }
        }

        public bool Match(string text, int offset, out int length, out int slot)
        {
            length = 0;
            slot = -1;
            if (text == null || offset < 0 || offset >= text.Length) return false;
            var node = _root;
            for (int i = offset; i < text.Length; i++)
            {
                if (!node.Next.TryGetValue(text[i], out node)) break;
                if (node.Slot < 0) continue;
                length = i - offset + 1;
                slot = node.Slot;
            }
            return slot >= 0;
        }

        public bool Contains(string text)
        {
            if (text == null) return false;
            for (int i = 0; i < text.Length; i++)
                if (Match(text, i, out _, out _)) return true;
            return false;
        }
    }
}
