using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Intercept the first OS close request to save before exit.
    // Cancel wantsToQuit and defer saving and Root.Shutdown until the next frame.
    // Keep external requests cancelled until the deferred shutdown starts.
    public static class QuitInterceptor
    {
        enum Phase { Idle, Pending, ShuttingDown }

        static Phase _phase;

        public static void Register()
        {
            Application.wantsToQuit += OnWantsToQuit;
        }

        // Called by Unity on the main thread when the OS wants to close the window.
        // Must return true to allow the quit, false to cancel it.
        static bool OnWantsToQuit()
        {
            // Programmatic shutdown (profile Quit button): let it through.
            if (SaveCoordinator.ConsumeProgrammaticShutdown()) return true;

            if (_phase == Phase.ShuttingDown) return true;
            if (_phase == Phase.Pending) return false;

            _phase = Phase.Pending;
            // Cancel this OS quit request.
            // Save and shut down on the next frame.
            return false;
        }

        // Patch_Root_Update calls this each frame.
        // Root.Shutdown owns the save through Patch_SaveOnShutdown.
        public static void Check()
        {
            if (_phase != Phase.Pending) return;
            _phase = Phase.ShuttingDown;
            Root.Shutdown();
        }
    }
}
