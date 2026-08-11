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

            TakeWheel(outer, max);
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
            Widgets.EndScrollView();

            // The scroll view moved it. That is where the list now is, and easing back
            // toward a target from before it would fight the hand on the bar.
            if (_pos != _drawn) _target = _pos;

            if (_bar) DrawBar();
        }

        // The bar's full width, and the thumb's inside it. Adwaita's is a slim slider with
        // clear air either side rather than a channel filled edge to edge; the callers all
        // reserve eighteen pixels off the view's width, so ten sits inside what is already
        // held back for it.
        const float BarW = 10f;
        const float ThumbPad = 2f;

        // Short enough to be a handle on a very long list, long enough to still be one.
        const float MinThumb = 24f;

        static readonly Color Trough = new Color(1f, 1f, 1f, 0.04f);
        static readonly Color Thumb = new Color(1f, 1f, 1f, 0.28f);
        static readonly Color ThumbOver = new Color(1f, 1f, 1f, 0.42f);
        static readonly Color ThumbHeld = new Color(1f, 1f, 1f, 0.55f);

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
            Slab.Fill(track, Trough);
            Slab.Fill(ThumbRect(track, h),
                held ? ThumbHeld : Mouse.IsOver(track) ? ThumbOver : Thumb);
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
