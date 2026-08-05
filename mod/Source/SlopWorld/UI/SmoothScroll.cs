using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A scroll position that is eased rather than assigned.
    //
    // IMGUI does the wheel in `GUI.EndScrollView`: it adds `delta * 20` to the position
    // and that is where the list is on the next frame. Fine for a mouse, where a notch is
    // one event and the jump reads as the notch. Wrong for a touchpad, where the driver
    // hands Unity the same notch-sized deltas dozens of times a gesture and the list
    // teleports in 60px steps with nothing between them.
    //
    // So the wheel is taken here first, before the scroll view sees it. The delta goes
    // into a *target* and what is drawn walks toward it, which costs a frame or two of
    // travel and buys a gesture that looks continuous. Sub-notch deltas - if a driver ever
    // sends them - add up in the target instead of being spent one at a time.
    //
    // Held by the caller in place of the `Vector2` it used to keep: the target and the
    // drawn position are two numbers and only one of them is the scroll view's.
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

        public void Begin(Rect outer, Rect view, bool showScrollbars = true)
        {
            var max = new Vector2(
                Mathf.Max(0f, view.width - outer.width),
                Mathf.Max(0f, view.height - outer.height));

            // Content can shrink under a target that was valid when it was set.
            _target.x = Mathf.Clamp(_target.x, 0f, max.x);
            _target.y = Mathf.Clamp(_target.y, 0f, max.y);

            TakeWheel(outer, max);
            Ease();

            _drawn = _pos;
            Widgets.BeginScrollView(outer, ref _pos, view, showScrollbars);
        }

        public void End()
        {
            Widgets.EndScrollView();

            // The scroll view moved it. That is where the list now is, and easing back
            // toward a target from before it would fight the hand on the bar.
            if (_pos != _drawn) _target = _pos;
        }

        void TakeWheel(Rect outer, Vector2 max)
        {
            var e = Event.current;
            if (e.type != EventType.ScrollWheel) return;

            // Nothing to scroll: leave the event for whatever is underneath. A box that
            // swallowed the wheel while showing its whole content would pin the page
            // behind it.
            if (max.x <= 0f && max.y <= 0f) return;
            if (!outer.Contains(e.mousePosition)) return;

            _target.x = Mathf.Clamp(_target.x + e.delta.x * Speed, 0f, max.x);
            _target.y = Mathf.Clamp(_target.y + e.delta.y * Speed, 0f, max.y);

            // Before `GUI.EndScrollView` gets it - ours and the scroll view's handling
            // would otherwise both land and the list would move twice as far. This also
            // stops an enclosing scroll view from taking the gesture off the inner one.
            e.Use();
        }

        void Ease()
        {
            if (_frame == Time.frameCount) return;
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
