using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class LinuxGameWindowTests
    {
        [TestCase(true, FullScreenMode.FullScreenWindow)]
        [TestCase(false, FullScreenMode.Windowed)]
        public void NativeFullscreenFollowsSavedMode(bool fullscreen, FullScreenMode expected)
        {
            var previous = Screen.fullScreenMode;
            var preference = Verse.Prefs.FullScreenMode;
            int saves = Verse.Prefs.Saves;
            try
            {
                Verse.Prefs.FullScreenMode = Screen.fullScreenMode =
                    FullScreenMode.ExclusiveFullScreen;
                LinuxGameWindow.ApplyNativeMode(fullscreen);
                LinuxGameWindow.ApplyNativeMode(fullscreen);
                Assert.That(Screen.fullScreenMode, Is.EqualTo(expected));
                Assert.That(Verse.Prefs.FullScreenMode, Is.EqualTo(expected));
                Assert.That(Verse.Prefs.Saves, Is.EqualTo(saves + 1));
            }
            finally
            {
                Screen.fullScreenMode = previous;
                Verse.Prefs.FullScreenMode = preference;
                Verse.Prefs.Saves = saves;
            }
        }
    }
}
