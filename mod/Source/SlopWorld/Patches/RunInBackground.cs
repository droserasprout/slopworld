using HarmonyLib;
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
}
