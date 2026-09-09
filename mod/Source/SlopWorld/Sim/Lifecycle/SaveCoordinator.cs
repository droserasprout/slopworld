using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    // Shared save and shutdown state for the autosaver and the window-close path. Keeping this
    // seam here lets either path request a save without knowing about the other.
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

        // Save failures must not block shutdown, but skip colonies pending NextPlanet so
        // AutoResume cannot restore a discarded map.
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
