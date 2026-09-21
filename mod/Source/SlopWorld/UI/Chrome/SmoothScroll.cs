using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Move directly by XInput's fractional touchpad distance where Linux exposes it, with
    // Unity's logical wheel packet as the portable fallback. Unity's stepped scroll view
    // stays out of the path either way.
    public sealed class SmoothScroll
    {
        // Wheel passes only change the retained offset. Flat list owners can omit row
        // work until repaint; retain normal passes for clicks, fields and scrollbar drags.
        // Check rawType too because BeginInput consumes the event before drawing content.
        public static bool WheelOnly => Event.current.type == EventType.ScrollWheel ||
            (Event.current.type == EventType.Used &&
                Event.current.rawType == EventType.ScrollWheel);

        // Keeps the two GUI groups and input ownership exception-safe. Use in a using block;
        // nested views remain well-formed even when a row renderer throws.
        public IDisposable Scope(Rect outer, Rect view, bool showScrollbars = true,
                                 bool preciseInput = true)
        {
            Begin(outer, view, showScrollbars, preciseInput);
            return new ScrollScope(this);
        }

        sealed class ScrollScope : IDisposable
        {
            SmoothScroll _owner;

            public ScrollScope(SmoothScroll owner) { _owner = owner; }

            public void Dispose()
            {
                if (_owner == null) return;
                var owner = _owner;
                _owner = null;
                owner.End();
            }
        }

        // Pixels per unit of wheel delta. Unity's own figure, and matching it is the point:
        // a notch is meant to travel the distance it has always travelled, just not all at
        // once.
        const float Speed = 20f;

        // XInput's increment is one wheel detent. Unity reports that detent as a delta of
        // two on Linux, so this preserves the ordinary wheel's established travel while
        // retaining every fractional touchpad movement inside the detent.
        const float X11Speed = Speed * 2f;

        // The bar's own geometry, kept from `Begin` because it is drawn in `End` - outside
        // the scroll view's group, which is the only place the outer rect means what it says.
        Rect _outer;
        Rect _measuredOuter;
        float _contentHeight = -1f;
        IDisposable _focusRegion;
        Vector2 _max;
        bool _bar;
        bool _preciseInput;

        // Where in the thumb the drag was started, so a grabbed bar does not jump its own
        // half-height under the cursor on the first frame.
        float _grab;

        Vector2 _pos;

        public Vector2 Position => _pos;

        // Put the list somewhere with no gesture behind it: a jump to a selected row is
        // not a scroll and should not be animated into one.
        public void JumpTo(Vector2 pos)
        {
            _pos = pos;
        }

        // The least travel that puts a row inside the viewport, and none at all if it is
        // already there. A jump rather than an ease for the reason `JumpTo` is one: this is
        // the keyboard moving a selection, and a highlight that arrives before the list it
        // is on reads as the wrong row being lit.
        public void Reveal(float top, float height, float viewport)
        {
            if (top < _pos.y) JumpTo(new Vector2(_pos.x, top));
            else if (top + height > _pos.y + viewport)
                JumpTo(new Vector2(_pos.x, top + height - viewport));
        }

        public void Begin(Rect outer, Rect view, bool showScrollbars = true,
                          bool preciseInput = true)
        {
            _measuredOuter = outer;
            _contentHeight = view.height;
            var origin = GUIUtility.GUIToScreenPoint(Vector2.zero);
            var max = new Vector2(
                Mathf.Max(0f, view.width - outer.width),
                Mathf.Max(0f, view.height - outer.height));

            BeginInput(outer, max, preciseInput);
            _bar = showScrollbars;
            _focusRegion = FieldFocusScope.TrackScroll(this, outer);

            // Do the clip and translation ourselves. Unity's scroll view processes wheel
            // input inside its native implementation, and can write a one-notch position
            // through its ref even after we consumed the event. Keeping it out of this path
            // makes `_pos` the only scroll state and leaves the wheel entirely to our input.
            GUI.BeginGroup(outer);
            GUI.BeginGroup(new Rect(view.x - _pos.x, view.y - _pos.y, view.width, view.height));
            ScrollWheelRouter.Begin(this, outer, view, preciseInput, origin);
        }

        // Terminal history has a moving window rather than a locally available document.
        // It still needs this class's input ownership and fractional wheel decoding, but it
        // draws its own frame and must not translate a GUI group around a synthetic document.
        public void BeginInput(Rect outer, Vector2 max, bool preciseInput = true)
        {
            // Content can shrink under a position that was valid on the previous frame.
            _pos.x = Mathf.Clamp(_pos.x, 0f, max.x);
            _pos.y = Mathf.Clamp(_pos.y, 0f, max.y);

            _preciseInput = preciseInput;
            if (_preciseInput) ClaimPrecise(outer, max);
            ClaimWheel(outer, max);

            _outer = outer;
            _max = max;
            _bar = false;
        }

        // Flat lists can route a wheel packet using the last measured extent. No row/model
        // traversal, GUI groups or control allocation is needed until the next normal pass.
        // Flat owners can use their last Begin measurement without maintaining a second
        // extent cache. Resize falls back to measurement; the first wheel still warms it.
        public bool HandleWheel(Rect outer) =>
            outer.Equals(_measuredOuter) && HandleWheel(outer, _contentHeight);

        public bool HandleWheel(Rect outer, float contentHeight)
        {
            if (!WheelOnly || contentHeight < 0f) return false;
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
            GUI.EndGroup();
            GUI.EndGroup();

            if (_bar) DrawBar();
        }

        public void EndInput()
        {
            if (_preciseInput) SpendPrecise();
            SpendWheel();
        }

        void DrawBar()
        {
            if (_max.y <= 0f) return;

            var track = UiScrollbar.Track(_outer);
            var hit = UiScrollbar.Hit(_outer, track);
            float h = UiScrollbar.ThumbHeight(track, _outer.height, _max.y);

            int id = GUIUtility.GetControlID(FocusType.Passive, hit);
            var e = Event.current;

            if (GUIUtility.hotControl == 0 && e.type == EventType.MouseDown && e.button == 0 &&
                hit.Contains(e.mousePosition))
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

            UiScrollbar.Draw(hit, track, UiScrollbar.Thumb(track, h, NormalizedPosition()),
                GUIUtility.hotControl == id);
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

        // Nested scroll views register in draw order; the last Begin claim is the innermost
        // box and receives the wheel. The event is muted before any content is drawn, so
        // no native widget can apply its own stepped wheel movement.
        static SmoothScroll _claim;
        static Vector2 _claimAmount;
        static bool _claimHasAmount;
        static Event _wheelEvent;

        // XInput is sampled once per rendered frame rather than once per IMGUI pass. The
        // same draw-order ownership rule as wheel events lets a nested list replace its
        // enclosing list before End spends the movement.
        static int _preciseFrame = -1;
        static bool _preciseAvailable;
        static bool _preciseSpent;
        static Vector2 _preciseAmount;
        static SmoothScroll _preciseClaim;
        static int _lastPreciseSpentFrame = -1;
        static Vector2 _lastPreciseSpentAmount;
        static Event _preciseWheelEvent;
        static Vector2 _preciseWheelDelta;
        static Vector2 _preciseWheelMouse;

        static bool PreciseHandled(Vector2 wheel)
        {
            if (_preciseAvailable && _preciseAmount.sqrMagnitude > 0.000001f &&
                (_preciseClaim != null || _preciseSpent)) return true;

            // The legacy button event can arrive one frame after the valuator movement
            // that caused it. Do not spend that same movement a second time, but only
            // suppress a matching direction so an unrelated wheel gesture still works.
            return _lastPreciseSpentFrame == Time.frameCount - 1 &&
                SameDirection(wheel, _lastPreciseSpentAmount);
        }

        static bool SameDirection(Vector2 a, Vector2 b)
        {
            bool x = Mathf.Abs(a.x) > 0.0001f && Mathf.Abs(b.x) > 0.0001f;
            bool y = Mathf.Abs(a.y) > 0.0001f && Mathf.Abs(b.y) > 0.0001f;
            return (x && Mathf.Sign(a.x) == Mathf.Sign(b.x)) ||
                (y && Mathf.Sign(a.y) == Mathf.Sign(b.y));
        }

        void ClaimPrecise(Rect outer, Vector2 max)
        {
            int frame = Time.frameCount;
            var e = Event.current;
            bool wheel = e.type == EventType.ScrollWheel;
            bool newWheel = wheel &&
                (!ReferenceEquals(_preciseWheelEvent, e) ||
                    _preciseWheelDelta.x != e.delta.x || _preciseWheelDelta.y != e.delta.y ||
                    _preciseWheelMouse.x != e.mousePosition.x ||
                    _preciseWheelMouse.y != e.mousePosition.y);
            bool refresh = newWheel && !_preciseSpent &&
                _preciseAmount.sqrMagnitude <= 0.000001f;
            if (_preciseFrame != frame || refresh)
            {
                _preciseFrame = frame;
                _preciseClaim = null;
                _preciseSpent = false;
                Vector2 units;
                _preciseAvailable = X11ScrollInput.TryRead(out units, wheel);
                _preciseAmount = units * X11Speed;
                if (wheel)
                {
                    _preciseWheelEvent = e;
                    _preciseWheelDelta = e.delta;
                    _preciseWheelMouse = e.mousePosition;
                }
                else _preciseWheelEvent = null;
            }

            if (!_preciseAvailable || _preciseSpent ||
                _preciseAmount.sqrMagnitude <= 0.000001f) return;
            if (max.x <= 0f && max.y <= 0f) return;
            if (!outer.Contains(Event.current.mousePosition)) return;
            _preciseClaim = this;
        }

        void ClaimWheel(Rect outer, Vector2 max)
        {
            var e = Event.current;
            if (e.type == EventType.ScrollWheel)
            {
                _claim = null;
                _claimHasAmount = false;
                _wheelEvent = e;
            }
            else if (e.type != EventType.Used || e.rawType != EventType.ScrollWheel ||
                !ReferenceEquals(_wheelEvent, e)) return;

            // Nothing to scroll: leave the event for whatever is underneath. A box that
            // swallowed the wheel while showing its whole content would pin the page
            // behind it.
            if (max.x <= 0f && max.y <= 0f) return;
            if (!outer.Contains(e.mousePosition)) return;

            _claim = this;
            // The first Begin reads the input. Nested Begins see the same Used event and
            // replace only the owner, not the already decoded pixel amount.
            if (!_claimHasAmount)
            {
                // A zero XInput sample is common when IMGUI ran a layout pass before the
                // actual wheel event arrived in this frame. Only suppress Unity when a
                // scroll view really claimed a non-zero precise sample; otherwise the
                // logical event is the input we have to spend.
                if (PreciseHandled(e.delta))
                {
                    // XInput already supplied this packet, including any sub-step parts.
                    _claimAmount = Vector2.zero;
                }
                else
                {
                    X11ScrollInput.DiscardPendingMovement();
                    var raw = Input.mouseScrollDelta;
                    _claimAmount = new Vector2(PrecisionDelta(e.delta.x, raw.x),
                        PrecisionDelta(e.delta.y, raw.y)) * Speed;
                }
                _claimHasAmount = true;
            }
            e.Use();
        }

        bool SpendPrecise()
        {
            if (_preciseClaim != this || _preciseSpent) return false;

            _preciseSpent = true;
            _preciseClaim = null;
            _lastPreciseSpentFrame = Time.frameCount;
            _lastPreciseSpentAmount = _preciseAmount;
            _pos = new Vector2(
                Mathf.Clamp(_pos.x + _preciseAmount.x, 0f, _max.x),
                Mathf.Clamp(_pos.y + _preciseAmount.y, 0f, _max.y));
            return true;
        }

        static float PrecisionDelta(float eventDelta, float rawDelta)
        {
            if (Mathf.Abs(rawDelta) <= 0.0001f) return eventDelta;
            if (Mathf.Abs(eventDelta) > 0.0001f &&
                Mathf.Sign(rawDelta) != Mathf.Sign(eventDelta)) return eventDelta;
            return Mathf.Abs(rawDelta) <= Mathf.Abs(eventDelta) + 0.0001f
                ? rawDelta : eventDelta;
        }

        // `End` runs innermost first, so the claimant is settled by the time it is reached.
        // Spent before the frame is finished so the next repaint sees the input immediately,
        // and using it there also keeps every enclosing view off the same gesture.
        bool SpendWheel()
        {
            var e = Event.current;
            if (_claim != this || !ReferenceEquals(_wheelEvent, e) ||
                (e.type != EventType.ScrollWheel && e.type != EventType.Used)) return false;

            _claim = null;
            _claimHasAmount = false;
            _wheelEvent = null;
            _pos = new Vector2(
                Mathf.Clamp(_pos.x + _claimAmount.x, 0f, _max.x),
                Mathf.Clamp(_pos.y + _claimAmount.y, 0f, _max.y));
            e.Use();
            return true;
        }
    }
}
