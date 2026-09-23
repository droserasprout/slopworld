using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    // Share save and shutdown state between autosaving and window closure.
    // Either caller can request a save without depending on the other.
    public static class SaveCoordinator
    {
        static bool _programmaticShutdown;

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

        // Save failures must not block shutdown.
        // Skip colonies with NextPlanet pending so AutoResume cannot restore a discarded map.
        public static void SaveNow()
        {
            try
            {
                if (NextPlanet.Pending) return;
                if (Current.ProgramState != ProgramState.Playing) return;
                Current.Game?.autosaver?.DoAutosave();
            }
            catch (Exception e)
            {
                Log.Error($"[SlopWorld] autosave failed: {e}");
            }
        }
    }
}
