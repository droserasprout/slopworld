using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A form retains one instance per opt-in area. Native WindowResizer owns the grip and
    // drag delta; this adapter owns vertical bounds, input capture and field lifetime.
    public sealed class UiAreaResize
    {
        internal const float CornerSize = 12f;
        internal static bool DrawingGrip { get; private set; }

        WindowResizer _resizer = new WindowResizer();
        readonly float _minimum;
        readonly float _maximum;
        readonly bool _grow;
        bool _manual;
        internal readonly SmoothScroll Scroll = new SmoothScroll();
        int _control;
        FieldLifetime _lifetime;
        EventType _gesture;

        public float Height { get; private set; }

        public bool CanGrow => _grow;
        public bool Automatic => _grow && !_manual;

        // Measurement is pure: callers use the result for both layout and drawing.
        public float MeasuredHeight(float contentHeight) => Automatic
            ? Mathf.Clamp(contentHeight, _minimum, Mathf.Max(_minimum, Mathf.Min(_maximum, 240f))) : Height;

        public void FitToContent()
        {
            Cancel();
            _manual = false;
        }

        public UiAreaResize(float height, float minimum = 48f, float maximum = 600f,
                            bool grow = false)
        {
            _grow = grow;
            _minimum = Mathf.Max(CornerSize, minimum);
            _maximum = Mathf.Max(_minimum, maximum);
            Height = Mathf.Clamp(height, _minimum, _maximum);
        }

        internal void Input(Rect rect, string name, bool enabled)
        {
            int control = GUIUtility.GetControlID(name.GetHashCode(), FocusType.Passive);
            var e = Event.current;
            EventType type = UiEvent.RawType(e);
            _gesture = EventType.Ignore;
            if (_control != 0 && (!enabled || GUIUtility.hotControl != _control ||
                !_lifetime.Alive || (type != EventType.MouseDown &&
                type != EventType.MouseDrag && type != EventType.MouseUp &&
                !InputHeld()))) Cancel();
            if (!enabled || !FieldLifetimeScope.Current.Alive) return;
            if ((type == EventType.MouseDown || type == EventType.MouseUp) &&
                e.button > 0) return;
            _gesture = type;

            var corner = new Rect(rect.xMax - CornerSize, rect.yMax - CornerSize,
                CornerSize, CornerSize);
            if (type == EventType.MouseDown && e.button <= 0 &&
                GUIUtility.hotControl == 0 && Mouse.IsOver(corner))
            {
                _lifetime = FieldLifetimeScope.Current;
                _lifetime.CancelResize?.Invoke();
                _lifetime.CancelResize = Cancel;
                _control = control;
                GUIUtility.hotControl = control;
            }
            if (_control == 0) return;
            // Claim input before the text editor runs. Complete the native control after
            // the text editor on every pass, so group/button control IDs stay consistent.
            if (type == EventType.MouseDown || type == EventType.MouseDrag ||
                type == EventType.MouseUp) e.Use();
        }

        static bool InputHeld() => UnityEngine.Input.GetMouseButton(0);

        internal void Complete(Rect rect, bool enabled)
        {
            if (!enabled) return;
            bool ownedMouse = _control != 0 && (_gesture == EventType.MouseDown ||
                _gesture == EventType.MouseDrag || _gesture == EventType.MouseUp);
            var type = ownedMouse ? _gesture : Event.current.type == EventType.Repaint
                ? EventType.Repaint : EventType.Ignore;
            float height = Native(rect, type).height;
            if (_control == 0) return;

            // WindowResizer invokes Widgets.ButtonImage -> GUI.Button. On a press that
            // button takes hotControl for itself; our gesture must keep the adapter's ID.
            GUIUtility.hotControl = _control;
            if (ownedMouse && (_gesture == EventType.MouseDrag || _gesture == EventType.MouseUp))
            {
                Height = Mathf.Clamp(height, _minimum, _maximum);
                if (_gesture == EventType.MouseDrag || height != rect.height) _manual = true;
            }
            if (ownedMouse && _gesture == EventType.MouseUp) Cancel();
        }

        Rect Native(Rect rect, EventType type)
        {
            _resizer.minWindowSize = new Vector2(rect.width, _minimum);
            bool previous = DrawingGrip;
            var previousColor = GUI.color;
            var e = Event.current;
            EventType previousType = e.type;
            // Replay owned raw events, including Used events with a cleared button. Idle
            // passes allocate the same native controls but cannot start a native gesture.
            e.type = type;
            GUI.BeginGroup(rect);
            try
            {
                DrawingGrip = true;
                // The native grip assumes a local (0, 0) origin. Only height is committed.
                return _resizer.DoResizeControl(new Rect(0f, 0f, rect.width, rect.height));
            }
            finally
            {
                DrawingGrip = previous;
                GUI.EndGroup();
                e.type = previousType;
                GUI.color = previousColor;
            }
        }

        void Cancel()
        {
            if (_control != 0 && GUIUtility.hotControl == _control) GUIUtility.hotControl = 0;
            _control = 0;
            // Native state is private and may still be dragging after a missed release.
            // Retire the whole resizer so the next gesture starts cleanly.
            _resizer = new WindowResizer();
            if (_lifetime != null) _lifetime.CancelResize = null;
            _lifetime = null;
        }

    }
}
