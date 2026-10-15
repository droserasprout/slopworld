using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    // Share save and shutdown state between autosaving and window closure.
    // Either caller can request a save without depending on the other.
    public static class SaveCoordinator
    {
        static bool _programmaticShutdown;
        static bool _shutdownSaved;

        public static void NoteProgrammaticShutdown()
        {
            _programmaticShutdown = true;
        }

        public static bool ConsumeProgrammaticShutdown()
        {
            bool programmatic = _programmaticShutdown;
            _programmaticShutdown = false;
            return programmatic;
        }

        // Root.Shutdown and the native quit lifecycle can both run for one exit.
        // Save synchronously before either path tears down the game, once per process.
        public static void SaveOnShutdown()
        {
            if (_shutdownSaved)
                return;
            _shutdownSaved = true;
            SaveNow();
        }

        // Save failures must not block shutdown.
        // Skip colonies with NextPlanet pending so AutoResume cannot restore a discarded map.
        public static void SaveNow()
        {
            try
            {
                if (LoadFailureRecovery.Pending)
                    return;
                if (NextPlanet.Pending)
                    return;
                if (Current.ProgramState != ProgramState.Playing)
                    return;
                Current.Game?.autosaver?.DoAutosave();
            }
            catch (Exception e)
            {
                Log.Error($"[SlopWorld] autosave failed: {e}");
            }
        }
    }
}
