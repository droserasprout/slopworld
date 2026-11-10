using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Adapt host music, SongDefs and game volume; Radio owns source and playback policy.
    internal static class NativeJukebox
    {
        static SongDef _pendingSong,
            _lastSong;
        static readonly System.Random Dice = new System.Random();
        static bool _openingTrackPending = ProfileAudioDefaults.FirstRun;

        // Explicit transport selections override the next song. Otherwise only the
        // first selection in a new profile overrides the manager's normal choice.
        [HarmonyPatch(typeof(MusicManagerPlay), "ChooseNextSong")]
        static class Patch_OpeningSong
        {
            static bool Prefix(ref SongDef __result)
            {
                if (_pendingSong != null)
                {
                    __result = _pendingSong;
                    _pendingSong = null;
                    _openingTrackPending = false;
                    return false;
                }
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

        // MusicUpdate initializes the audio source and marks completed songs idle.
        // Advance after that work to bypass vanilla's random inter-track delay.
        // Radio disables this manager while muted or using daemon playback.
        [HarmonyPatch(typeof(MusicManagerPlay), nameof(MusicManagerPlay.MusicUpdate))]
        static class Patch_ContinuousPlayback
        {
            static void Prefix(MusicManagerPlay __instance)
            {
                if (!Radio.NativePlaybackAllowed)
                {
                    __instance.disabled = true;
                    __instance.Stop();
                }
            }

            static void Postfix(MusicManagerPlay __instance)
            {
                if (!__instance.disabled && !__instance.IsPlaying)
                    __instance.StartNewSong();
            }
        }

        internal static void Navigate(int direction, bool random)
        {
            var songs = DefDatabase<SongDef>
                .AllDefsListForReading.Where(song =>
                    song.clipPath != null
                    && song.clipPath.StartsWith("SlopWorld/OST/", StringComparison.Ordinal)
                )
                .ToList();
            var music = NativeMusic();
            var current = _pendingSong ?? music?.CurrentSong ?? _lastSong;
            int index = RadioNavigation.Index(
                songs.Count,
                songs.IndexOf(current),
                direction,
                random ? Dice : null
            );
            if (index < 0)
                return;
            _pendingSong = songs[index];
            if (Radio.NativePlaybackAllowed)
            {
                music?.Stop();
                Resume();
            }
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
            {
                _lastSong = music.CurrentSong ?? _lastSong;
                music.Stop();
            }
            else if (!muted && changed)
                Resume();
        }

        // Restart the selected SlopWorld song immediately after unpausing.
        // Clearing disabled alone waits for the native music transition timer.
        internal static void Resume()
        {
            if (!Radio.NativePlaybackAllowed)
                return;
            var music = NativeMusic();
            if (music == null)
                return;

            _pendingSong = _pendingSong ?? _lastSong;
            music.disabled = false;
            if (Current.ProgramState == ProgramState.Playing)
                music.StartNewSong();
        }

        internal static float Volume => Mathf.Clamp01(Prefs.VolumeMusic * Prefs.VolumeMaster);
    }
}
