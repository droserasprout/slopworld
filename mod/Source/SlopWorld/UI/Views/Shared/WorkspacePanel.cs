using System;

namespace SlopWorld
{
    public readonly struct PanelSize
    {
        public readonly float Width, Height;
        public PanelSize(float width, float height)
        {
            Width = Math.Max(0f, width);
            Height = Math.Max(0f, height);
        }
    }

    // Identity belongs to the instance, not its title or session name. Minimum size is
    // a placement preference. A viewport smaller than it must still remain bounded.
    public interface IWorkspacePanel
    {
        string PanelId { get; }
        PanelSize MinimumSize { get; }
        void Opened();
        void Closed();
        void VisibilityChanged(bool visible);
        void FocusChanged(bool focused);
        void Arrange(UiLayoutRect bounds);
    }

    // The single-slot workspace retains its backing terminal while content covers it.
    // Visibility/focus changes do not close retained panels or discard their state.
    public sealed class WorkspacePanelOwner<T> where T : class, IWorkspacePanel
    {
        public T Backing { get; private set; }
        public T Content { get; private set; }
        public T Active => Content ?? Backing;
        // Host focus persists while focus transfers between retained panels.
        public bool Focused { get; private set; }

        public void SetBacking(T panel)
        {
            if (panel != null && ReferenceEquals(Content, panel))
                throw new ArgumentException("Panel already occupies the content slot", nameof(panel));
            if (ReferenceEquals(Backing, panel)) return;
            var previous = Active;
            if (Content == null) Hide(previous);
            Backing?.Closed();
            Backing = panel;
            panel?.Opened();
            if (Content == null) Show(Active);
        }

        public void SetContent(T panel)
        {
            if (panel != null && ReferenceEquals(Backing, panel))
                throw new ArgumentException("Panel already occupies the backing slot", nameof(panel));
            if (ReferenceEquals(Content, panel)) return;
            Hide(Active);
            Content?.Closed();
            Content = panel;
            panel?.Opened();
            Show(Active);
        }

        public void SetFocus(bool focused)
        {
            if (Focused == focused) return;
            Focused = focused;
            Active?.FocusChanged(focused);
        }

        public void Arrange(UiLayoutRect bounds) => Active?.Arrange(bounds);

        public void Close()
        {
            Hide(Active);
            Content?.Closed();
            Backing?.Closed();
            Content = Backing = null;
            Focused = false;
        }

        void Hide(T panel)
        {
            if (panel == null) return;
            if (Focused) panel.FocusChanged(false);
            panel.VisibilityChanged(false);
        }

        void Show(T panel)
        {
            if (panel == null) return;
            panel.VisibilityChanged(true);
            if (Focused) panel.FocusChanged(true);
        }
    }
}
