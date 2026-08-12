using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Intercept OS close requests so the game saves before exit: cancel wantsToQuit, save on
    // the next frame, then call Root.Shutdown. A second request and programmatic shutdown pass.
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
