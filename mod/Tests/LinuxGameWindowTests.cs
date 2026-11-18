using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class LinuxGameWindowTests
    {
        const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        [TestCase(FullScreenMode.ExclusiveFullScreen)]
        [TestCase(FullScreenMode.FullScreenWindow)]
        public void NativeModesAreResetWithoutChangingSavedWindowManagerFullscreen(
            FullScreenMode mode
        )
        {
            var previousMode = Screen.fullScreenMode;
            var preference = Verse.Prefs.FullScreenMode;
            int saves = Verse.Prefs.Saves;
            bool fullscreen = Settings.S.fullscreen;
            try
            {
                Screen.fullScreenMode = Verse.Prefs.FullScreenMode = mode;
                typeof(LinuxGameWindow)
                    .GetMethod("EnsureWindowed", PrivateStatic)
                    .Invoke(null, null);
                Assert.That(Screen.fullScreenMode, Is.EqualTo(FullScreenMode.Windowed));
                Assert.That(Verse.Prefs.FullScreenMode, Is.EqualTo(FullScreenMode.Windowed));
                Assert.That(Settings.S.fullscreen, Is.EqualTo(fullscreen));
                Assert.That(Verse.Prefs.Saves, Is.EqualTo(saves + 1));
                typeof(LinuxGameWindow)
                    .GetMethod("EnsureWindowed", PrivateStatic)
                    .Invoke(null, null);
                Assert.That(
                    Verse.Prefs.Saves,
                    Is.EqualTo(saves + 1),
                    "settled state does not rewrite preferences"
                );
            }
            finally
            {
                Screen.fullScreenMode = previousMode;
                Verse.Prefs.FullScreenMode = preference;
                Verse.Prefs.Saves = saves;
            }
        }

        [Test]
        public void OtherPlatformsSkipFollowBeforeStartupOrNativeWork()
        {
            var platform = Application.platform;
            var recovery = (LinuxWindowRecovery)
                typeof(LinuxGameWindow).GetField("Recovery", PrivateStatic).GetValue(null);
            try
            {
                Application.platform = RuntimePlatform.WindowsPlayer;
                LinuxGameWindow.Follow();
                Assert.That(recovery.Applied, Is.Null);
                Assert.That(recovery.NextTry, Is.Zero);
            }
            finally
            {
                Application.platform = platform;
            }
        }

        [Test]
        public void SurfaceGeometryRequiresValidMonitorDimensionsAndMatchingUnitySurface()
        {
            int width = Screen.width,
                height = Screen.height;
            try
            {
                Screen.width = 1920;
                Screen.height = 1080;
                Assert.That(LinuxGameWindow.SurfaceSized((1920, 1080)), Is.True);
                Assert.That(LinuxGameWindow.SurfaceSized((2560, 1440)), Is.False);
                Assert.That(LinuxGameWindow.SurfaceSized((1920, 1200)), Is.False);
                Assert.That(LinuxGameWindow.SurfaceSized((0, 1080)), Is.False);
                Assert.That(LinuxGameWindow.SurfaceSized((1920, 0)), Is.False);
            }
            finally
            {
                Screen.width = width;
                Screen.height = height;
            }
        }
    }
}
