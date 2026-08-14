using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Intercept wheel input and ease a target toward the drawn position, smoothing touchpad
    // bursts while preserving Unity's 20px-per-wheel-unit travel.
    public sealed class SmoothScroll
    {
        // Pixels per unit of wheel delta. Unity's own figure, and matching it is the point:
        // a notch is meant to travel the distance it has always travelled, just not all at
        // once.
        const float Speed = 20f;

        // Seconds for the remaining distance to fall to 1/e of itself. Long enough to read
        // as motion, short enough that a click on a row that has just arrived lands on it.
        const float Tau = 0.05f;

        // Below this the ease is over. A position that only ever approaches its target
        // keeps the window repainting for nothing.
        const float Snap = 0.5f;

        // The bar's own geometry, kept from `Begin` because it is drawn in `End` - outside
        // the scroll view's group, which is the only place the outer rect means what it says.
        Rect _outer;
        Vector2 _max;
        bool _bar;

        // Where in the thumb the drag was started, so a grabbed bar does not jump its own
        // half-height under the cursor on the first frame.
        float _grab;

        Vector2 _pos;
        Vector2 _target;

        // What the scroll view was handed this frame, so its own writes through the `ref`
        // - a scrollbar drag, or a clamp against content that shrank - can be told from
        // our easing and taken as the new target.
        Vector2 _drawn;

        // IMGUI runs several event passes per frame and the ease gets one step of them.
        int _frame = -1;

        public Vector2 Position => _pos;

        // Put the list somewhere with no gesture behind it: a jump to a selected row is
        // not a scroll and should not be animated into one.
        public void JumpTo(Vector2 pos)
        {
            _pos = _target = pos;
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

        public void Begin(Rect outer, Rect view, bool showScrollbars = true)
        {
            var max = new Vector2(
                Mathf.Max(0f, view.width - outer.width),
                Mathf.Max(0f, view.height - outer.height));

            // Content can shrink under a target that was valid when it was set.
            _target.x = Mathf.Clamp(_target.x, 0f, max.x);
            _target.y = Mathf.Clamp(_target.y, 0f, max.y);

            ClaimWheel(outer, max);
            Ease();

            _outer = outer;
            _max = max;
            _bar = showScrollbars;

            _drawn = _pos;
            // Always false: the bar is drawn in `End`. Handing `true` here is what put the
            // Unity skin's bar - the last vanilla widget in the mod - inside every list.
            Widgets.BeginScrollView(outer, ref _pos, view, false);
        }

        public void End()
        {
            bool wheel = SpendWheel();
            // Wheel ownership is resolved here so an inner list can win, but that is after
            // Begin's easing step. Move once now as well, or the repaint for this event
            // draws the old position and the gesture starts a frame late.
            if (wheel) Ease(true);
            Widgets.EndScrollView();

            // The scroll view moved it. That is where the list now is, and easing back
            // toward a target from before it would fight the hand on the bar.
            if (!wheel && _pos != _drawn) _target = _pos;

            if (_bar) DrawBar();
        }

        // The bar has clear air either side instead of filling its reserved gutter. Every
        // caller reserves [SlopWidgets.ScrollbarW], so the narrow steel track lines up across
        // windows, trees and menus.
        const float BarW = SlopWidgets.ScrollTrackW;
        const float ThumbPad = SlopWidgets.ScrollThumbInset;

        // Short enough to be a handle on a very long list, long enough to still be one.
        const float MinThumb = 24f;

        // Vertical only. Nothing here scrolls sideways - every caller sizes its view to the
        // outer rect's width less the bar - and a bar drawn for an axis with no travel in it
        // is a control that cannot move.
        void DrawBar()
        {
            if (_max.y <= 0f) return;

            var track = new Rect(_outer.xMax - BarW, _outer.y, BarW, _outer.height);
            float h = ThumbH(track);

            int id = GUIUtility.GetControlID(FocusType.Passive, track);
            var e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0 &&
                track.Contains(e.mousePosition))
            {
                // On the thumb, it is picked up where it was touched. On the trough, it
                // arrives centred under the cursor - which is a jump to that point in the
                // list, and then a drag from it without letting go.
                var at = ThumbRect(track, h);
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

            bool held = GUIUtility.hotControl == id;
            Slab.Fill(track, SlopWidgets.ScrollTrough);
            Slab.Fill(ThumbRect(track, h),
                held ? SlopWidgets.ScrollThumbHeld : Mouse.IsOver(track)
                    ? SlopWidgets.ScrollThumbHover : SlopWidgets.ScrollThumb);
        }

        float ThumbH(Rect track) =>
            Mathf.Clamp(track.height * (track.height / (track.height + _max.y)),
                Mathf.Min(MinThumb, track.height), track.height);

        Rect ThumbRect(Rect track, float h)
        {
            float t = _max.y <= 0f ? 0f : Mathf.Clamp01(_pos.y / _max.y);
            return new Rect(track.x + ThumbPad, track.y + (track.height - h) * t,
                track.width - ThumbPad * 2f, h);
        }

        // A hand on the bar is not a gesture to ease: the list goes where the thumb is put,
        // this frame, and the target goes with it so the ease has nothing left to do.
        void DragTo(float mouseY, Rect track, float h)
        {
            float span = track.height - h;
            float t = span <= 0f ? 0f : Mathf.Clamp01((mouseY - _grab - track.y) / span);
            _pos.y = _target.y = t * _max.y;
        }

        // Nested scroll views register in draw order; the last Begin claim is the innermost box and receives the wheel.
        static SmoothScroll _claim;
        static Vector2 _claimDelta;

        void ClaimWheel(Rect outer, Vector2 max)
        {
            var e = Event.current;
            if (e.type != EventType.ScrollWheel) return;

            // Nothing to scroll: leave the event for whatever is underneath. A box that
            // swallowed the wheel while showing its whole content would pin the page
            // behind it.
            if (max.x <= 0f && max.y <= 0f) return;
            if (!outer.Contains(e.mousePosition)) return;

            _claim = this;
            _claimDelta = e.delta;
        }

        // `End` runs innermost first, so the claimant is settled by the time it is reached.
        // Spent before `GUI.EndScrollView` gets the event - ours and the scroll view's
        // handling would otherwise both land and the list would move twice as far - and
        // using it there also keeps every enclosing view off the same gesture.
        bool SpendWheel()
        {
            var e = Event.current;
            if (_claim != this || e.type != EventType.ScrollWheel) return false;

            _claim = null;
            _target.x = Mathf.Clamp(_target.x + _claimDelta.x * Speed, 0f, _max.x);
            _target.y = Mathf.Clamp(_target.y + _claimDelta.y * Speed, 0f, _max.y);
            e.Use();
            return true;
        }

        void Ease(bool force = false)
        {
            if (!force && _frame == Time.frameCount) return;
            _frame = Time.frameCount;

            var d = _target - _pos;
            if (d.sqrMagnitude <= Snap * Snap)
            {
                _pos = _target;
                return;
            }

            // Unscaled: the game being paused is not the list standing still.
            _pos += d * (1f - Mathf.Exp(-Time.unscaledDeltaTime / Tau));
        }
    }
}
