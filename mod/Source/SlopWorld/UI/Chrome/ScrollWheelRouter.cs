using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Retain only scroll geometry between ordinary IMGUI passes. Wheel storms then cost
    // O(scroll regions), not O(rows/fields). Capture the tree so inner owners still claim
    // input before their parents spend it, using the current parent scroll translation.
    public sealed class ScrollWheelRouter
    {
        sealed class Region
        {
            public SmoothScroll Scroll;
            public Rect Outer;
            public Rect View;
            public bool Precise;
            // Screen position of local zero after entering this region's scroll group.
            public Vector2 ScreenOrigin;
            public readonly List<Region> Children = new List<Region>();

            public void Replay()
            {
                using (Scroll.Scope(Outer, View, false, Precise))
                    foreach (var child in Children) child.Replay();
            }
        }

        static ScrollWheelRouter _capturing;
        readonly Region _root = new Region();
        readonly Stack<Region> _parents = new Stack<Region>();
        Rect _bounds;
        bool _ready;
        bool _captureSupported;

        public void Invalidate() => _ready = false;

        public void Draw(Rect bounds, Action draw)
        {
            // An enclosing router records the complete tree, including this subtree.
            if (_capturing != null) { draw(); return; }
            var origin = GUIUtility.GUIToScreenPoint(new Vector2());
            if (SmoothScroll.WheelOnly && _ready && bounds.Equals(_bounds) && origin.Equals(_root.ScreenOrigin))
            {
                foreach (var region in _root.Children) region.Replay();
                return;
            }

            bool wheel = SmoothScroll.WheelOnly;
            _ready = false;
            _captureSupported = true;
            _bounds = bounds;
            _root.ScreenOrigin = origin;
            _root.Children.Clear();
            _parents.Clear();
            _parents.Push(_root);
            var previous = _capturing;
            _capturing = this;
            try
            {
                draw();
                // Existing wheel fast paths may omit their scroll scopes entirely.
                // Only an ordinary pass can publish a complete tree.
                _ready = !wheel && _captureSupported && _parents.Count == 1;
            }
            finally
            {
                _capturing = previous;
                _parents.Clear();
            }
        }

        // Called after entering the scroll group; origin is the pre-group screen origin.
        internal static void Begin(SmoothScroll scroll, Rect outer, Rect view,
                                   bool precise, Vector2 origin)
        {
            var router = _capturing;
            if (router == null) return;
            var parent = router._parents.Peek();
            // Arbitrary intervening GUI groups may add clipping or transforms we cannot
            // replay. Keep the ordinary path for those renderers rather than misroute input.
            if (!parent.ScreenOrigin.Equals(origin)) router._captureSupported = false;
            var region = new Region
            {
                Scroll = scroll,
                Outer = outer,
                View = view,
                Precise = precise,
                ScreenOrigin = GUIUtility.GUIToScreenPoint(new Vector2())
            };
            parent.Children.Add(region);
            router._parents.Push(region);
        }

        internal static void End()
        {
            if (_capturing != null) _capturing._parents.Pop();
        }
    }
}
