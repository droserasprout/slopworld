using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Patch the setter because PrefsData.Apply reads the stored field.
    // Changing only the getter would not keep Application.runInBackground enabled when preferences apply.
    [HarmonyPatch(typeof(Prefs), nameof(Prefs.RunInBackground), MethodType.Setter)]
    public static class Patch_RunInBackground
    {
        static void Prefix(ref bool value) => value = true;

        // Apply the setting after patching because Prefs.Init runs before mod initialization.
        // Queue the change on the main thread because Prefs.Apply does nothing on other threads.
        public static void Enforce() => LongEventHandler.ExecuteWhenFinished(() =>
        {
            if (Prefs.RunInBackground) return;
            Prefs.RunInBackground = true; // forced true above. The log line is the point
            Prefs.Save();
            Log.Message("[SlopWorld] run in background turned on: the board has to keep up");
        });
    }

    // Keep foreground frame timing independent of Eco. Apply the background limit when the application loses focus.
    public static class BackgroundFrames
    {
        static readonly FramePolicy Policy = new FramePolicy();

        // Root.Update calls this in menus and during play. Update settings only when the effective values change.
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
