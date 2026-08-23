using UnityEngine;

namespace SlopWorld
{
    // Non-terminal content drawn inside the chrome, right of the sidebar and below the top
    // bar. These are views rather than windows so no absorbing window blocks sidebar input.
    // Draw also handles input; IMGUI has no separate input pass.
    public interface IContentView
    {
        // What the top bar calls this, where a pane would have named its agent.
        string Title { get; }

        // The body rect, in screen coordinates. Called once a frame per event.
        void Draw(Rect body);

        // Shown and hidden. Neither is a constructor: a view is built when it is asked for
        // and dropped when it is left, so what these carry is the work that has to happen
        // *around* that - a page re-reading config.toml, a settings file written once.
        void Opened();
        void Closed();
    }

    // An options page is built lazily, loaded once, then handed its rect. Config-backed pages
    // use Load to read their source, while local pages can make it a no-op; the shared shape
    // lets SlopOptions dispatch every category from the same tab table.
    public interface IOptionPage
    {
        void Load();
        void Draw(Rect rect);
    }
}
