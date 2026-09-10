using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // One owner per form lifetime. Restore focus only when the form regains input;
    // keyboard traversal is deferred and this scope does not consume Tab events.
    sealed class FieldFocusScope : IDisposable
    {
        internal sealed class Memory
        {
            public readonly FieldFocusOrder Order = new FieldFocusOrder();
            public bool Eligible;
            public int LastFrame = -1;
            internal readonly List<Target> Targets = new List<Target>();
        }

        internal sealed class Target
        {
            public string Name;
            public int Id;
            public readonly List<Reveal> Scrolls = new List<Reveal>();
        }

        internal struct Reveal
        {
            public SmoothScroll Scroll;
            public float Top, Height, Viewport, Scale;
        }

        internal sealed class ScrollRegion : IDisposable
        {
            internal readonly SmoothScroll Scroll;
            internal readonly Vector2 Origin;
            internal readonly float Scale, Height, Offset;
            readonly ScrollRegion _previous;
            internal ScrollRegion(SmoothScroll scroll, Rect outer)
            {
                Scroll = scroll;
                Origin = GUIUtility.GUIToScreenPoint(outer.position);
                Scale = Mathf.Abs(GUIUtility.GUIToScreenPoint(outer.position + Vector2.up).y - Origin.y);
                Height = outer.height;
                Offset = scroll.Position.y;
                _previous = _scroll;
                _scroll = this;
            }
            public void Dispose() { _scroll = _previous; }
            internal ScrollRegion Previous => _previous;
        }

        static FieldFocusScope _current;
        static ScrollRegion _scroll;
        readonly FieldFocusScope _previous;
        readonly FieldLifetime _lifetime;
        readonly Memory _memory;
        readonly Window _window;
        readonly bool _eligible, _restore;
        int _targetCount;

        public FieldFocusScope(FieldLifetime lifetime, bool eligible)
        {
            _previous = _current;
            _current = this;
            _lifetime = lifetime;
            _memory = lifetime.Focus;
            if (_memory.LastFrame < Time.frameCount - 1) _memory.Eligible = false;
            _memory.LastFrame = Time.frameCount;
            _window = Find.WindowStack?.currentlyDrawnWindow;
            _eligible = eligible;
            var e = Event.current;
            _restore = eligible && !_memory.Eligible && e.rawType != EventType.MouseDown;
            if (_restore && _memory.Order.Restore() != null) GUIUtility.keyboardControl = 0;
            _memory.Eligible = eligible;
            _memory.Order.Begin();
        }

        public static IDisposable TrackScroll(SmoothScroll scroll, Rect outer) =>
            _current == null ? null : new ScrollRegion(scroll, outer);

        public static void Register(string name, int id, Rect rect)
        {
            var scope = _current;
            if (scope == null || !scope._lifetime.Alive || !GUI.enabled) return;
            scope._memory.Order.Register(name);
            if (scope._eligible && !scope._restore && GUIUtility.keyboardControl == id)
                scope._memory.Order.Remember(name);
            var targets = scope._memory.Targets;
            if (scope._targetCount == targets.Count) targets.Add(new Target());
            var target = targets[scope._targetCount++];
            target.Name = name;
            target.Id = id;
            target.Scrolls.Clear();
            var top = GUIUtility.GUIToScreenPoint(rect.position);
            var bottom = GUIUtility.GUIToScreenPoint(new Vector2(rect.x, rect.yMax));
            for (var scroll = _scroll; scroll != null; scroll = scroll.Previous)
            {
                if (scroll.Scale <= 0.001f) continue;
                target.Scrolls.Add(new Reveal
                {
                    Scroll = scroll.Scroll,
                    Top = (top.y - scroll.Origin.y) / scroll.Scale + scroll.Offset,
                    Height = (bottom.y - top.y) / scroll.Scale,
                    Viewport = scroll.Height,
                    Scale = scroll.Scale,
                });
            }
        }

        public void Dispose()
        {
            _current = _previous;
            if (_window != null && !Find.WindowStack.GetsInput(_window))
            {
                _memory.Eligible = false;
                return;
            }
            if (!_eligible || !_lifetime.Alive) return;
            string name = _restore ? _memory.Order.Restore() : null;
            if (name == null) return;
            Target target = null;
            for (int i = 0; i < _targetCount; i++)
                if (_memory.Targets[i].Name == name) { target = _memory.Targets[i]; break; }
            if (target == null) return;
            TextFieldSelection.ReleaseFocus();
            GUI.FocusControl(name);
            GUIUtility.keyboardControl = target.Id;
            // Account for inner scrolling before revealing the field in an outer viewport.
            float screenShift = 0f;
            foreach (var reveal in target.Scrolls)
            {
                float before = reveal.Scroll.Position.y;
                reveal.Scroll.Reveal(reveal.Top - screenShift / reveal.Scale,
                    Mathf.Min(reveal.Height, reveal.Viewport), reveal.Viewport);
                screenShift += (reveal.Scroll.Position.y - before) * reveal.Scale;
            }
        }
    }
}
