using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Intercepts OS-level close requests (Alt+F4, window close button on Linux) to
    // save the game before the process exits.  Unity fires Application.wantsToQuit
    // when the window manager sends a close request.  Without this, the process exits
    // without going through Root.Shutdown, so Patch_SaveOnShutdown never fires and the
    // colony is not saved.
    //
    // The close request is cancelled, the game is saved on the next frame, and then
    // Root.Shutdown is called (which saves again via Patch_SaveOnShutdown and quits
    // cleanly).  A second close request while the save is in flight is let through.
    public static class QuitInterceptor
    {
        // null = idle, "pending" = save and quit on next frame
        static string _state;
        // Set before calling Root.Shutdown, so the wantsToQuit event that fires when
        // Root.Shutdown calls Application.Quit is let through instead of looping.
        static bool _shuttingDown;
        // Set by Patch_SaveOnShutdown: a programmatic Root.Shutdown (daemon restart,
        // profile Quit button) should not be intercepted by the wantsToQuit handler.
        static bool _programmatic;

        public static void Register()
        {
            Application.wantsToQuit += OnWantsToQuit;
        }

        // Called by Patch_SaveOnShutdown to mark that this Root.Shutdown is from a
        // programmatic path (daemon restart, profile Quit button) and should not be
        // intercepted.
        public static void NoteProgrammaticShutdown()
        {
            _programmatic = true;
        }

        // Called by Unity on the main thread when the OS wants to close the window.
        // Must return true to allow the quit, false to cancel it.
        static bool OnWantsToQuit()
        {
            // Already shutting down from a previous close request: let the second one
            // through to avoid blocking the exit.
            if (_state != null || _shuttingDown) return true;

            // Programmatic shutdown (daemon restart, profile Quit button): let it through.
            if (_programmatic)
            {
                _programmatic = false;
                return true;
            }

            _state = "pending";
            // Cancel the OS quit; the save and clean shutdown happen on the next frame.
            return false;
        }

        // Called every frame from Patch_Root_Update.  Saves the game and calls
        // Root.Shutdown to quit cleanly.
        public static void Check()
        {
            if (_state != "pending") return;
            _state = null;

            _shuttingDown = true;
            AutoSaver.SaveNow();
            Root.Shutdown();
        }
    }
}