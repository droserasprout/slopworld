using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The setter is patched rather than the getter, because what reaches Unity is
    // PrefsData.Apply reading the *field*: a getter that lied would leave
    // Application.runInBackground false the next time anything applied prefs.
    [HarmonyPatch(typeof(Prefs), nameof(Prefs.RunInBackground), MethodType.Setter)]
    public static class Patch_RunInBackground
    {
        static void Prefix(ref bool value) => value = true;

        // Prefs.Init has been and gone before a mod patches anything, so this is the only way
        // in. Queued: Prefs.Apply is a no-op off the main thread, where static constructors run.
        public static void Enforce() => LongEventHandler.ExecuteWhenFinished(() =>
        {
            if (Prefs.RunInBackground) return;
            Prefs.RunInBackground = true; // forced true above; the log line is the point
            Prefs.Save();
            Log.Message("[SlopWorld] run in background turned on: the board has to keep up");
        });
    }

    // Use a background cap for catch-up and a gentler Eco cap for pane input; restore target FPS
    // and vSync together when leaving either state.
    public static class BackgroundFrames
    {
        const int Away = 15;
        const int Rest = 30;

        // The cap in force, or zero for the machine's own setting.
        static int _capped;
        static int _wasTarget;
        static int _wasVSync;

        // Off Root.Update, menu and game alike. Only the edges do anything.
        public static void Follow()
        {
            int want = !Application.isFocused ? Away : (Eco.Resting ? Rest : 0);
            if (want == _capped) return;

            // Whatever was found is worth keeping only on the way in from uncapped: the
            // second edge of an eco spell that began behind another window would otherwise
            // save the cap over the setting it is meant to go back to.
            if (_capped == 0)
            {
                _wasTarget = Application.targetFrameRate;
                _wasVSync = QualitySettings.vSyncCount;
            }

            if (want == 0)
            {
                Application.targetFrameRate = _wasTarget;
                QualitySettings.vSyncCount = _wasVSync;
            }
            else
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = want;
            }

            _capped = want;
        }
    }
}
