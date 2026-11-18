using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Retain fractional wheel movement across nested scroll views.
    public sealed class SmoothScroll
    {
        // Wheel passes only change the retained offset. Flat list owners can omit row
        // work until repaint. Retain normal passes for clicks, fields and scrollbar drags.
        // Check rawType too because BeginInput consumes the event before drawing content.
        public static bool WheelOnly =>
            Event.current.type == EventType.ScrollWheel
            || (
                Event.current.type == EventType.Used
                && Event.current.rawType == EventType.ScrollWheel
            );

        // Keeps the two GUI groups and input ownership exception-safe. Use in a using block.
        // nested views remain well-formed even when a row renderer throws.
        public IDisposable Scope(Rect outer, Rect view, bool showScrollbars = true)
        {
            Begin(outer, view, showScrollbars);
            return new ScrollScope(this);
        }

        sealed class ScrollScope : IDisposable
        {
            SmoothScroll _owner;

            public ScrollScope(SmoothScroll owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                if (_owner == null)
                    return;
                var owner = _owner;
                _owner = null;
                owner.End();
            }
        }

        // Pixels per unit of wheel delta. Unity's own figure, and matching it is the point. A notch
        // is meant to travel the distance it has always travelled, just not all at once.
        const float Speed = 20f;

        // The bar's own geometry, kept from `Begin` because it is drawn in `End` - outside the
        // scroll view's group. This is the only place the outer rect means what it says.
        Rect _outer;
        Rect _measuredOuter;
        float _contentHeight = -1f;
        IDisposable _focusRegion;
        Vector2 _max;
        bool _bar;

        // Where in the thumb the drag was started, so a grabbed bar does not jump its own
        // half-height under the cursor on the first frame.
        float _grab;

        Vector2 _pos;

        public Vector2 Position => _pos;

        // Record consumed wheel movement for latency measurements.
        internal Action<Vector2, Vector2, string> ObserveInput;

        // Put the list somewhere with no gesture behind it. A jump to a selected row is not a
        // scroll and should not be animated into one.
        public void JumpTo(Vector2 pos)
        {
            _pos = pos;
        }

        // The least travel that puts a row inside the viewport, and none at all if it is already
        // there. A jump rather than an ease for the reason `JumpTo` is one. This is the keyboard
        // moving a selection, and a highlight that arrives before the list it is on reads as the
        // wrong row being lit.
        public void Reveal(float top, float height, float viewport)
        {
            if (top < _pos.y)
                JumpTo(new Vector2(_pos.x, top));
            else if (top + height > _pos.y + viewport)
                JumpTo(new Vector2(_pos.x, top + height - viewport));
        }

        public void Begin(Rect outer, Rect view, bool showScrollbars = true)
        {
            _measuredOuter = outer;
            _contentHeight = view.height;
            var origin = GUIUtility.GUIToScreenPoint(Vector2.zero);
            var max = new Vector2(
                Mathf.Max(0f, view.width - outer.width),
                Mathf.Max(0f, view.height - outer.height)
            );

            BeginInput(outer, max);
            _bar = showScrollbars;
            _focusRegion = FieldFocusScope.TrackScroll(this, outer);

            // Do the clip and translation ourselves. Unity's scroll view processes wheel
            // input inside its native implementation, and can write a one-notch position
            // through its ref even after we consumed the event. Keeping it out of this path
            // makes `_pos` the only scroll state and leaves the wheel entirely to our input.
            // RimWorld's wrappers refresh the Linux/Steam Deck event pointer after
            // each translation. Raw GUI groups skip that repair on keyboard passes.
            Widgets.BeginGroup(outer);
            Widgets.BeginGroup(new Rect(view.x - _pos.x, view.y - _pos.y, view.width, view.height));
            ScrollWheelRouter.Begin(this, outer, view, origin);
        }

        // Terminal history has a moving window rather than a locally available document. It still
        // needs this class's input ownership and fractional wheel decoding. It draws its own
        // frame and must not translate a GUI group around a synthetic document.
        public void BeginInput(Rect outer, Vector2 max)
        {
            // Content can shrink under a position that was valid on the previous frame.
            _pos.x = Mathf.Clamp(_pos.x, 0f, max.x);
            _pos.y = Mathf.Clamp(_pos.y, 0f, max.y);

            // Windows underneath menus draw first. Reject their claims before either
            // native touchpad samples or wheel packets can move the covered content.
            // This also reserves non-scrolling menus and their padding for the menu.
            bool blocked = UiMenu.BlocksScrollBehind();
            if (!blocked)
            {
                ClaimWheel(outer, max);
            }

            _outer = outer;
            _max = max;
            _bar = false;
        }

        // Flat lists can route a wheel packet using the last measured extent. No row/model
        // traversal, GUI groups or control allocation is needed until the next normal pass.
        // Flat owners can use their last Begin measurement without maintaining a second
        // extent cache. Resize falls back to measurement. The first wheel still warms it.
        public bool HandleWheel(Rect outer) =>
            outer.Equals(_measuredOuter) && HandleWheel(outer, _contentHeight);

        public bool HandleWheel(Rect outer, float contentHeight)
        {
            if (!WheelOnly || contentHeight < 0f)
                return false;
            BeginInput(outer, new Vector2(0f, Mathf.Max(0f, contentHeight - outer.height)));
            EndInput();
            return true;
        }

        public void End()
        {
            ScrollWheelRouter.End();
            _focusRegion?.Dispose();
            _focusRegion = null;
            EndInput();
            Widgets.EndGroup();
            Widgets.EndGroup();

            if (_bar)
                DrawBar();
        }

        public void EndInput()
        {
            SpendWheel();
        }

        void DrawBar()
        {
            if (_max.y <= 0f)
                return;

            var track = UiScrollbar.Track(_outer);
            var hit = UiScrollbar.Hit(_outer, track);
            float h = UiScrollbar.ThumbHeight(track, _outer.height, _max.y);

            int id = GUIUtility.GetControlID(FocusType.Passive, hit);
            var e = Event.current;

            if (
                GUIUtility.hotControl == 0
                && e.type == EventType.MouseDown
                && e.button == 0
                && hit.Contains(e.mousePosition)
            )
            {
                var at = UiScrollbar.Thumb(track, h, NormalizedPosition());
                _grab = at.Contains(e.mousePosition) ? e.mousePosition.y - at.y : h / 2f;
                GUIUtility.hotControl = id;
                DragTo(e.mousePosition.y, track, h);
                e.Use();
            }
            else if (GUIUtility.hotControl == id)
            {
                if (e.type == EventType.MouseDrag)
                {
                    DragTo(e.mousePosition.y, track, h);
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && e.button == 0)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                }
            }

            UiScrollbar.Draw(
                hit,
                track,
                UiScrollbar.Thumb(track, h, NormalizedPosition()),
                GUIUtility.hotControl == id
            );
        }

        float NormalizedPosition() => _max.y <= 0f ? 0f : Mathf.Clamp01(_pos.y / _max.y);

        // A hand on the bar is direct manipulation: the list goes where the thumb is put,
        // this frame, with no leftover motion.
        void DragTo(float mouseY, Rect track, float h)
        {
            float span = track.height - h;
            float t = span <= 0f ? 0f : Mathf.Clamp01((mouseY - _grab - track.y) / span);
            _pos.y = t * _max.y;
        }

        // Nested scroll views register in draw order. The last Begin claim is the innermost
        // box and receives the wheel. The event is muted before any content is drawn, so
        // no native widget can apply its own stepped wheel movement.
        sealed class WheelClaim
        {
            public SmoothScroll Owner;
            public Vector2 Amount;
            public bool HasAmount;
            public Event Event;
        }

        static readonly WheelClaim Wheel = new WheelClaim();

        void ClaimWheel(Rect outer, Vector2 max)
        {
            var e = Event.current;
            if (e.type == EventType.ScrollWheel)
            {
                Wheel.Owner = null;
                Wheel.HasAmount = false;
                Wheel.Event = e;
            }
            else if (
                e.type != EventType.Used
                || e.rawType != EventType.ScrollWheel
                || !ReferenceEquals(Wheel.Event, e)
            )
                return;

            // Nothing to scroll: leave the event for whatever is underneath. A box that
            // swallowed the wheel while showing its whole content would pin the page
            // behind it.
            if (max.x <= 0f && max.y <= 0f)
                return;
            if (!outer.Contains(e.mousePosition))
                return;

            Wheel.Owner = this;
            // The first Begin reads the input. Nested Begins see the same Used event and
            // replace only the owner, not the already decoded pixel amount.
            if (!Wheel.HasAmount)
            {
                var raw = Input.mouseScrollDelta;
                Wheel.Amount =
                    new Vector2(PrecisionDelta(e.delta.x, raw.x), PrecisionDelta(e.delta.y, raw.y))
                    * Speed;
                Wheel.HasAmount = true;
            }
            e.Use();
        }

        static float PrecisionDelta(float eventDelta, float rawDelta)
        {
            if (Mathf.Abs(rawDelta) <= 0.0001f)
                return eventDelta;
            if (Mathf.Abs(eventDelta) > 0.0001f && Mathf.Sign(rawDelta) != Mathf.Sign(eventDelta))
                return eventDelta;
            return Mathf.Abs(rawDelta) <= Mathf.Abs(eventDelta) + 0.0001f ? rawDelta : eventDelta;
        }

        // `End` runs innermost first, so the claimant is settled by the time it is reached.
        // Spent before the frame is finished so the next repaint sees the input immediately,
        // and using it there also keeps every enclosing view off the same gesture.
        bool SpendWheel()
        {
            var e = Event.current;
            if (
                Wheel.Owner != this
                || !ReferenceEquals(Wheel.Event, e)
                || (e.type != EventType.ScrollWheel && e.type != EventType.Used)
            )
                return false;

            Wheel.Owner = null;
            Wheel.HasAmount = false;
            Wheel.Event = null;
            var next = new Vector2(
                Mathf.Clamp(_pos.x + Wheel.Amount.x, 0f, _max.x),
                Mathf.Clamp(_pos.y + Wheel.Amount.y, 0f, _max.y)
            );
            ObserveInput?.Invoke(_pos, next, "wheel");
            _pos = next;
            e.Use();
            return true;
        }
    }
}
