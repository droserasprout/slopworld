using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Adapt host music, SongDefs and game volume; Radio owns source and playback policy.
    internal static class NativeJukebox
    {
        static bool _openingTrackPending = ProfileAudioDefaults.FirstRun;

        // Override only the first selection in a new profile. The manager retains
        // its normal history, inter-track timing, and subsequent song selection.
        [HarmonyPatch(typeof(MusicManagerPlay), "ChooseNextSong")]
        static class Patch_OpeningSong
        {
            static bool Prefix(ref SongDef __result)
            {
                if (!_openingTrackPending)
                    return true;
                _openingTrackPending = false;
                var song = DefDatabase<SongDef>.GetNamedSilentFail("SlopWorld_pace_20261012");
                if (song == null)
                    return true;
                __result = song;
                return false;
            }
        }

        internal static string OstPath()
        {
            var root = ModEntry.Instance?.Content?.RootDir;
            return string.IsNullOrEmpty(root)
                ? null
                : System.IO.Path.Combine(root, "Sounds", "SlopWorld", "OST");
        }

        static RimWorld.MusicManagerPlay NativeMusic()
        {
            try
            {
                return Find.MusicManagerPlay;
            }
            catch
            {
                return null;
            } // Root_Entry has no play music manager.
        }

        internal static NativeTrackSnapshot ReadNativeTrack()
        {
            try
            {
                var music = NativeMusic();
                var song = music?.CurrentSong;
                if (music == null || !music.IsPlaying || song == null)
                    return null;

                return NativeTrackSnapshot.FromOstClipPath(song.clipPath);
            }
            catch
            {
                return null;
            }
        }

        internal static void SetMuted(bool muted)
        {
            var music = NativeMusic();
            if (music == null)
                return;

            bool changed = music.disabled != muted;
            music.disabled = muted;
            if (muted && (changed || music.IsPlaying))
                music.Stop();
            else if (!muted && changed)
                Resume();
        }

        // Start the next SlopWorld song immediately after unmuting.
        // Clearing disabled alone waits for the native music transition timer.
        internal static void Resume()
        {
            var music = NativeMusic();
            if (music == null)
                return;

            music.disabled = false;
            if (Current.ProgramState == ProgramState.Playing)
                music.StartNewSong();
        }

        internal static float Volume => Mathf.Clamp01(Prefs.VolumeMusic * Prefs.VolumeMaster);

        internal static void DisableForDaemon()
        {
            if (Current.ProgramState == ProgramState.Playing)
            {
                try
                {
                    var music = Find.MusicManagerPlay;
                    if (music != null && !music.disabled)
                    {
                        music.Stop();
                        music.disabled = true;
                    }
                }
                catch
                { /* Root_Play castclass fails on menu */
                }
            }
        }
    }
}
