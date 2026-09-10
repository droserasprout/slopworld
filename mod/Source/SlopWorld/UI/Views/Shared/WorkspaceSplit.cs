using System;

namespace SlopWorld
{
    // Retained children share visibility, but only one owns keyboard input.
    public sealed class WorkspaceSplit<T> where T : class, IWorkspacePanel
    {
        public T First { get; private set; }
        public T Second { get; private set; }
        public T Selected { get; private set; }
        bool _opened, _visible, _focused;

        public WorkspaceSplit(T first) { First = Selected = first; }
        public void Open()
        {
            if (_opened) return;
            _opened = true;
            First.Opened();
            Second?.Opened();
        }
        public void Select(T panel)
        {
            if (panel == null || (panel != First && panel != Second) || panel == Selected) return;
            if (_focused) Selected.FocusChanged(false);
            Selected = panel;
            if (_focused) Selected.FocusChanged(true);
        }
        public void Add(T panel)
        {
            if (Second != null) throw new InvalidOperationException("Split already has two panels");
            Second = panel;
            if (_opened) panel.Opened();
            if (_visible) panel.VisibilityChanged(true);
            Select(panel);
        }
        public void Remove(T panel)
        {
            if (Second == null || (panel != First && panel != Second)) return;
            var remaining = panel == First ? Second : First;
            Select(remaining);
            if (_visible) panel.VisibilityChanged(false);
            if (_opened) panel.Closed();
            First = remaining;
            Second = null;
        }
        public void SetFocus(bool focused)
        {
            if (_focused == focused) return;
            _focused = focused;
            Selected.FocusChanged(focused);
        }
        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            if (!visible) SetFocus(false);
            _visible = visible;
            First.VisibilityChanged(visible);
            Second?.VisibilityChanged(visible);
        }
        public void Close()
        {
            SetFocus(false);
            SetVisible(false);
            if (!_opened) return;
            _opened = false;
            First.Closed();
            Second?.Closed();
        }
    }

    public readonly struct WorkspaceSplitGeometry
    {
        public readonly UiLayoutRect First, Divider, Second;
        public WorkspaceSplitGeometry(UiLayoutRect bounds, float fraction, float gap, float minimum)
        {
            gap = Math.Min(bounds.Width, Math.Max(0f, gap));
            float available = bounds.Width - gap;
            float min = Math.Min(Math.Max(0f, minimum), available / 2f);
            float left = Math.Max(min, Math.Min(available - min, available * fraction));
            First = new UiLayoutRect(bounds.X, bounds.Y, left, bounds.Height);
            Divider = new UiLayoutRect(bounds.X + left, bounds.Y, gap, bounds.Height);
            Second = new UiLayoutRect(Divider.XMax, bounds.Y, available - left, bounds.Height);
        }
    }
}
