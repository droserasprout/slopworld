using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The backing view keeps both terminals alive while a full-workspace view covers it.
    sealed class TerminalSplit : ContentView
    {
        readonly ITerminalPanelHost _host;
        readonly Action<string> _selected;
        readonly WorkspaceSplit<TerminalPanel> _panes;
        float _fraction = 0.5f;
        int _dividerControl;
        bool _dragging;
        public TerminalPanel Selected => _panes.Selected;
        public TerminalPanel First => _panes.First;
        public TerminalPanel Second => _panes.Second;
        public bool Split => Second != null;
        public override string Title => Selected.Title;

        public TerminalSplit(ITerminalPanelHost host, string name, Action<string> selected)
        {
            _host = host;
            _selected = selected;
            _panes = new WorkspaceSplit<TerminalPanel>(new TerminalPanel(host, name));
        }

        public TerminalPanel Find(string name) => First.SessionName == name ? First :
            Second?.SessionName == name ? Second : null;

        public void Select(TerminalPanel panel)
        {
            if (panel == Selected) return;
            _panes.Select(panel);
            _selected(Selected.SessionName);
        }

        public void OpenSplit(string name)
        {
            var existing = Find(name);
            if (existing != null) { Select(existing); return; }
            if (Second != null)
            {
                Select(Selected == First ? Second : First);
                Selected.BindSession(name);
            }
            else
            {
                var panel = new TerminalPanel(_host, name);
                // Assign its half before opening/subscribing, so its initial resize is correct.
                var geometry = Geometry(Bounds);
                panel.Arrange(geometry.Second);
                _panes.Add(panel);
            }
            Arrange(Bounds);
            _selected(name);
            TerminalRecall.Remember(name);
        }

        public void Remove(TerminalPanel panel)
        {
            EndDrag();
            _panes.Remove(panel);
            Arrange(Bounds);
            _selected(Selected.SessionName);
        }

        public override void Opened() => _panes.Open();
        public override void Closed() { EndDrag(); _panes.Close(); }
        public override void VisibilityChanged(bool visible)
        {
            if (!visible) EndDrag();
            _panes.SetVisible(visible);
            base.VisibilityChanged(visible);
        }
        public override void FocusChanged(bool focused)
        {
            if (!focused) EndDrag();
            _panes.SetFocus(focused);
            base.FocusChanged(focused);
        }

        WorkspaceSplitGeometry Geometry(UiLayoutRect bounds) =>
            new WorkspaceSplitGeometry(bounds, _fraction, UiTheme.GapS,
                First.MinimumSize.Width);

        public override void Arrange(UiLayoutRect bounds)
        {
            base.Arrange(bounds);
            if (!Split) { First.Arrange(bounds); return; }
            var geometry = Geometry(bounds);
            First.Arrange(geometry.First);
            Second.Arrange(geometry.Second);
        }

        public override void Draw(Rect body)
        {
            var e = Event.current;
            if (Split)
            {
                var divider = RectOf(Geometry(Bounds).Divider);
                _dividerControl = GUIUtility.GetControlID(FocusType.Passive);
                var mouse = TerminalWindow.MouseType(e);
                if (Focused && GUIUtility.hotControl == 0 &&
                    mouse == EventType.MouseDown && e.button == 0 &&
                    divider.Contains(e.mousePosition))
                {
                    _panes.SetFocus(false);
                    _dragging = true;
                    GUIUtility.hotControl = _dividerControl;
                    e.Use();
                }
                if (_dragging)
                {
                    if (mouse == EventType.MouseDrag)
                    {
                        float available = Mathf.Max(1f, Bounds.Width - divider.width);
                        _fraction = Mathf.Clamp01((e.mousePosition.x - Bounds.X - divider.width / 2f) / available);
                        Arrange(Bounds);
                        e.Use();
                    }
                    if (mouse == EventType.MouseUp && e.button == 0)
                    {
                        EndDrag();
                        // Restore terminal input on the next pass, not this consumed release.
                        e.Use();
                    }
                }
                else if (Focused)
                {
                    _panes.SetFocus(true);
                    if (mouse == EventType.MouseDown && GUIUtility.hotControl == 0)
                    {
                        if (RectOf(First.Bounds).Contains(e.mousePosition)) Select(First);
                        else if (RectOf(Second.Bounds).Contains(e.mousePosition)) Select(Second);
                    }
                }
            }

            // Draw the unfocused child first. A navigation event in the focused child may
            // change selection or close the split. Its replacement must not see that event.
            var selected = Selected;
            var other = selected == First ? Second : First;
            if (other != null) other.Draw(RectOf(other.Bounds));
            if (Visible) selected.Draw(RectOf(selected.Bounds));
            if (Split)
            {
                Slab.Fill(RectOf(Geometry(Bounds).Divider), _dragging ? UiTheme.EdgeLit : UiTheme.Edge);
                var bounds = Selected.Bounds;
                Slab.Fill(new Rect(bounds.X, bounds.Y, bounds.Width, 1f), UiTheme.EdgeLit);
            }
        }

        void EndDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            if (GUIUtility.hotControl == _dividerControl) GUIUtility.hotControl = 0;
        }

        public void Update() { First.Update(); Second?.Update(); }
        public void Flush() { First.Flush(); Second?.Flush(); }
        static Rect RectOf(UiLayoutRect r) => new Rect(r.X, r.Y, r.Width, r.Height);
    }
}
