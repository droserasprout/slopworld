using System;
using NUnit.Framework;
using Verse;

namespace SlopWorld.Tests
{
    static class ProfileAudioDefaultsTests
    {
        public static void AudioDefaultsPreserveDetectedScaleAndExistingPreferences()
        {
            string saved = Environment.GetEnvironmentVariable("SLOPWORLD_INITIAL_AUDIO");
            float scale = Prefs.UIScale;
            float master = Prefs.VolumeMaster,
                game = Prefs.VolumeGame,
                music = Prefs.VolumeMusic;
            float ambient = Prefs.VolumeAmbient,
                ui = Prefs.VolumeUI;
            int saves = Prefs.AudioPreferenceSaves;
            try
            {
                Prefs.UIScale = 2f;
                Environment.SetEnvironmentVariable("SLOPWORLD_INITIAL_AUDIO", "1");
                ProfileAudioDefaults.Apply();
                Assert.That(Prefs.UIScale, Is.EqualTo(2f));
                Assert.That(Prefs.VolumeMaster, Is.EqualTo(0.8f));
                Assert.That(Prefs.VolumeGame, Is.EqualTo(1f));
                Assert.That(Prefs.VolumeMusic, Is.EqualTo(0.8f));
                Assert.That(Prefs.VolumeAmbient, Is.EqualTo(0f));
                Assert.That(Prefs.VolumeUI, Is.EqualTo(0.3f));
                Assert.That(Prefs.AudioPreferenceSaves, Is.EqualTo(saves + 1));
                Prefs.VolumeMaster = 0.4f;
                foreach (string value in new string[] { "0", null })
                {
                    Environment.SetEnvironmentVariable("SLOPWORLD_INITIAL_AUDIO", value);
                    ProfileAudioDefaults.Apply();
                    Assert.That(Prefs.VolumeMaster, Is.EqualTo(0.4f));
                    Assert.That(Prefs.AudioPreferenceSaves, Is.EqualTo(saves + 1));
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("SLOPWORLD_INITIAL_AUDIO", saved);
                Prefs.UIScale = scale;
                Prefs.VolumeMaster = master;
                Prefs.VolumeGame = game;
                Prefs.VolumeMusic = music;
                Prefs.VolumeAmbient = ambient;
                Prefs.VolumeUI = ui;
                Prefs.AudioPreferenceSaves = saves;
            }
        }
    }
}
