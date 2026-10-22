using System;
using Verse;

namespace SlopWorld
{
    // The launcher identifies a new profile before RimWorld initializes Prefs.
    // Apply only audio afterward, preserving detected resolution and UI scale.
    internal static class ProfileAudioDefaults
    {
        internal static bool FirstRun =>
            Environment.GetEnvironmentVariable("SLOPWORLD_INITIAL_AUDIO") == "1";

        internal static void Apply()
        {
            if (!FirstRun)
                return;

            Prefs.VolumeMaster = 0.8f;
            Prefs.VolumeGame = 1f;
            Prefs.VolumeMusic = 0.8f;
            Prefs.VolumeAmbient = 0f;
            Prefs.VolumeUI = 0.3f;
            Prefs.Save();
        }
    }
}
