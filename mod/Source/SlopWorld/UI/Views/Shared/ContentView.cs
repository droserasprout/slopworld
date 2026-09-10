using UnityEngine;

namespace SlopWorld
{
    // Panels draw in host-assigned screen coordinates. IMGUI drawing also handles input.
    public interface IContentView : IWorkspacePanel
    {
        // What the top bar calls this, where a pane would have named its agent.
        string Title { get; }

        // The body rect, in screen coordinates. Called once a frame per event.
        void Draw(Rect body);

    }

    public abstract class ContentView : IContentView
    {
        public string PanelId { get; } = System.Guid.NewGuid().ToString("N");
        public virtual PanelSize MinimumSize => new PanelSize(320f, 160f);
        public UiLayoutRect Bounds { get; private set; }
        public bool Visible { get; private set; }
        public bool Focused { get; private set; }
        public abstract string Title { get; }
        public abstract void Draw(Rect body);
        public virtual void Opened() { }
        public virtual void Closed() { }
        public virtual void Arrange(UiLayoutRect bounds) { Bounds = bounds; }
        public virtual void VisibilityChanged(bool visible) { Visible = visible; }
        public virtual void FocusChanged(bool focused) { Focused = focused; }
    }

    // An options page is built lazily, loaded once, then handed its rect. Config-backed pages
    // use Load to read their source, while local pages can make it a no-op; the shared shape
    // lets ModOptions dispatch every category from the same tab table.
    public interface IOptionPage
    {
        void Load();
        void Draw(Rect rect);
    }
}
