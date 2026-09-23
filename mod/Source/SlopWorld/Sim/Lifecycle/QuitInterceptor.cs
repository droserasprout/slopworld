using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Intercept the first OS close request to save before exit.
    // Cancel wantsToQuit and defer saving and Root.Shutdown until the next frame.
    // Permit repeated requests and programmatic shutdown.
    public static class QuitInterceptor
    {
        // null = idle, "pending" = save and quit on next frame
        static string _state;
        // Set before Root.Shutdown so its Application.Quit call can pass through wantsToQuit without repeating the save sequence.
        static bool _shuttingDown;

        public static void Register()
        {
            Application.wantsToQuit += OnWantsToQuit;
        }

        // Called by Unity on the main thread when the OS wants to close the window.
        // Must return true to allow the quit, false to cancel it.
        static bool OnWantsToQuit()
        {
            // Permit another close request when shutdown is pending or active.
            if (_state != null || _shuttingDown) return true;

            // Programmatic shutdown (profile Quit button): let it through.
            if (SaveCoordinator.ConsumeProgrammaticShutdown()) return true;

            _state = "pending";
            // Cancel this OS quit request.
            // Save and shut down on the next frame.
            return false;
        }

        // Patch_Root_Update calls this each frame.
        // For a pending request, attempt to save before calling Root.Shutdown.
        public static void Check()
        {
            if (_state != "pending") return;
            _state = null;

            _shuttingDown = true;
            SaveCoordinator.SaveNow();
            Root.Shutdown();
        }
    }
}
