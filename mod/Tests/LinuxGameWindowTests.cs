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
                Assert.That(recovery.ReadySince, Is.EqualTo(-1f));
            }
            finally
            {
                Application.platform = platform;
            }
        }

        [Test]
        public void SurfaceGeometryRequiresValidMonitorDimensionsAndMatchingUnitySurface()
        {
            bool SurfaceSized() =>
                (bool)
                    typeof(LinuxGameWindow)
                        .GetMethod("SurfaceSized", PrivateStatic)
                        .Invoke(null, null);
            int width = Screen.width,
                height = Screen.height;
            int monitorWidth = Display.main.systemWidth,
                monitorHeight = Display.main.systemHeight;
            try
            {
                Display.main.systemWidth = Screen.width = 1920;
                Display.main.systemHeight = Screen.height = 1080;
                Assert.That(SurfaceSized(), Is.True);
                Screen.height = 1000;
                Assert.That(SurfaceSized(), Is.False);
                Screen.height = 1080;
                Screen.width = 1800;
                Assert.That(SurfaceSized(), Is.False);
                Screen.width = 1920;
                Display.main.systemHeight = 0;
                Assert.That(SurfaceSized(), Is.False);
                Display.main.systemHeight = 1080;
                Display.main.systemWidth = 0;
                Assert.That(SurfaceSized(), Is.False);
            }
            finally
            {
                Screen.width = width;
                Screen.height = height;
                Display.main.systemWidth = monitorWidth;
                Display.main.systemHeight = monitorHeight;
            }
        }
    }
}
