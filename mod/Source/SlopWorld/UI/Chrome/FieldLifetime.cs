using System;

namespace SlopWorld
{
    // Clipboard replies arrive after the IMGUI pass that requested them. A control id and
    // name are useful guards, but neither says whether the window that owned the field still
    // exists. Owners get a lifetime token and cancel it when they close or change content.
    public sealed class FieldLifetime
    {
        bool _alive = true;
        internal readonly FieldFocusScope.Memory Focus = new FieldFocusScope.Memory();

        public bool Alive => _alive;

        public void Cancel()
        {
            if (!_alive) return;
            _alive = false;
            TextFieldSelection.Retire(this);
        }
    }

    // IMGUI has no current Window argument while a control is being drawn. The window/content
    // boundary pushes its token for the duration of the draw. Therefore, every field and every
    // delayed clipboard operation below inherits the same owner without repeating it at each call
    // site.
    internal static class FieldLifetimeScope
    {
        // Deliberate fallback for unscoped controls. Content that can close or be replaced
        // must push its own lifetime and cancel it at that boundary.
        static readonly FieldLifetime ProcessLifetime = new FieldLifetime();

        [ThreadStatic]
        static FieldLifetime _current;

        public static FieldLifetime Current => _current ?? ProcessLifetime;

        public static IDisposable Push(FieldLifetime lifetime)
        {
            var previous = _current;
            _current = lifetime ?? ProcessLifetime;
            return new Scope(previous);
        }

        sealed class Scope : IDisposable
        {
            readonly FieldLifetime _previous;

            public Scope(FieldLifetime previous) { _previous = previous; }

            public void Dispose() { _current = _previous; }
        }
    }
}
