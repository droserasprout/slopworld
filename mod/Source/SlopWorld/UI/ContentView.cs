using UnityEngine;

namespace SlopWorld
{
    // What fills the room the chrome leaves: right of the column, under the line. One of
    // these is what the window is *for*, and the sidebar and the top bar are what is around
    // it whichever one it is.
    //
    // An agent's pane, the host's shell and the viewer's `less` are all the same content -
    // a terminal on a session - and the window draws that one itself rather than through
    // this interface, the pane being what it was built as. Everything else that used to
    // open as a window over the chrome is one of these: the options menu, and the three
    // lists. The point is that none of them is a *window* any more, so nothing absorbs
    // input above the column and every press on it arrives (see gotchas.md - a window under
    // an absorbing one is never called for a MouseDown, and that is what had the tabs dead
    // while the options menu was up).
    //
    // IMGUI, so Draw is also where the clicks are taken: there is no separate input pass.
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
}
