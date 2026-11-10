using System;

namespace SlopWorld
{
    // Carries the caller's text policy across Waylith's backend API, which has no
    // richText argument. Keep source strings and UTF-16 selection offsets intact.
    internal readonly struct NativeTextScope : IDisposable
    {
        [ThreadStatic]
        static bool _literal;

        readonly bool _previous;

        internal static bool Literal => _literal;

        internal NativeTextScope(bool richText)
        {
            _previous = _literal;
            _literal = !richText;
        }

        public void Dispose() => _literal = _previous;
    }
}
