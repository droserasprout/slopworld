namespace SlopWorld
{
    public static partial class Radio
    {
        // Own source transitions. Catalog restoration preserves mute; explicit choices
        // unmute, persist and invalidate playback/metadata through this shared handoff.
        static Station _station;
        static bool _spotify, _muted;

        static void SelectSource(Station station = null, int rate = 0,
            bool spotify = false, bool clearFailure = true)
        {
            _station = station;
            _spotify = spotify;
            if (station != null) station.Rate = rate;
            _muted = false;
            if (clearFailure) _blamed = false;
            Save();
            Push();
        }

    }
}
