using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Vanilla makes this a preference and defaults it off, which is right for a
    // colony sim and wrong for a board over processes that run whether the window is
    // up or not.
    //
    // The setter is patched, not the getter, because what reaches Unity is
    // PrefsData.Apply reading the *field*: a getter that lied would leave
    // Application.runInBackground false the next time anything applied prefs.
    [HarmonyPatch(typeof(Prefs), nameof(Prefs.RunInBackground), MethodType.Setter)]
    public static class Patch_RunInBackground
    {
        static void Prefix(ref bool value) => value = true;

        // Nothing calls the setter on its own and Prefs.Init has been and gone long
        // before a mod patches anything, so this is the only way in. Queued rather than
        // done here: Prefs.Apply is a no-op off the main thread and mod static
        // constructors do not run on it.
        public static void Enforce() => LongEventHandler.ExecuteWhenFinished(() =>
        {
            if (Prefs.RunInBackground) return;
            Prefs.RunInBackground = true; // forced true above; the log line is the point
            Prefs.Save();
            Log.Message("[SlopWorld] run in background turned on: the board has to keep up");
        });
    }

    // The half that makes running in the background affordable: keeping up is not the
    // same as drawing sixty times a second at a window nobody is looking at.
    //
    // The sim does not slow with the frames. TickManagerUpdate banks Time.deltaTime and
    // spends up to 45ms a frame paying it back, so fifteen frames a second is four ticks
    // a frame at exact pace. Below ten it stops banking - the accumulator is assigned
    // rather than added once deltaTime reaches 0.1 - hence the clearance.
    //
    // vSync comes off with it or the cap does nothing: Unity ignores targetFrameRate
    // while vSyncCount is set. Both are put back as found, vanilla having written
    // targetFrameRate once in Root.CheckGlobalInit.
    public static class BackgroundFrames
    {
        const int Fps = 15;

        static bool _capped;
        static int _wasTarget;
        static int _wasVSync;

        // Off Root.Update, menu and game alike. Only the edges do anything.
        public static void Follow()
        {
            bool want = !Application.isFocused;
            if (want == _capped) return;

            if (want)
            {
                _wasTarget = Application.targetFrameRate;
                _wasVSync = QualitySettings.vSyncCount;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = Fps;
            }
            else
            {
                Application.targetFrameRate = _wasTarget;
                QualitySettings.vSyncCount = _wasVSync;
            }

            _capped = want;
        }
    }
}
