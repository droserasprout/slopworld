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

    // Foreground pacing is independent of Eco; only loss of focus forces the background cap.
    public static class BackgroundFrames
    {
        static readonly FramePolicy Policy = new FramePolicy();

        // Off Root.Update, menu and game alike. Write only when the effective pair changes.
        public static void Follow()
        {
            int target = Application.targetFrameRate, sync = QualitySettings.vSyncCount;
            if (!Policy.Follow(Application.isFocused, Settings.DisplayMode, Settings.ForegroundFps,
                    ref target, ref sync)) return;
            QualitySettings.vSyncCount = sync;
            Application.targetFrameRate = target;
        }
    }
}
