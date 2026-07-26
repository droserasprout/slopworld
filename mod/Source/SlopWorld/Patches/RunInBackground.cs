using HarmonyLib;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The game goes on ticking with its window behind something else. Vanilla
    /// makes that a preference and defaults it off, which is right for a colony
    /// sim - nothing happens in one while you are not watching, so nothing is
    /// lost. Here the map is a status board over processes that run whether the
    /// window is up or not, and a board that freezes the moment you go and read
    /// the code an agent wrote is a board reporting the past. So it is enforced
    /// rather than offered, the same way the UI stripping is: being loaded is the
    /// switch.
    ///
    /// The setter is what is patched, not the getter, because what actually
    /// reaches Unity is PrefsData.Apply reading the *field*: a getter that lied
    /// would leave Application.runInBackground false the next time anything
    /// applied prefs. Forcing the value on the way in means the Options checkbox
    /// reads back on whichever way it is clicked, and the file it saves says so.
    /// </summary>
    [HarmonyPatch(typeof(Prefs), nameof(Prefs.RunInBackground), MethodType.Setter)]
    public static class Patch_RunInBackground
    {
        static void Prefix(ref bool value) => value = true;

        /// <summary>
        /// Turns it on for a prefs file that has it off, which is every file nobody
        /// has been through the options with. Nothing calls the setter on its own,
        /// and Prefs.Init has been and gone long before a mod patches anything, so
        /// this is the only way in.
        ///
        /// Queued rather than done here: Prefs.Apply is a no-op off the main thread
        /// and mod static constructors do not run on it, so setting the field from
        /// one would leave Unity carrying the old answer until something else
        /// applied prefs. ExecuteWhenFinished lands on the main thread, where the
        /// setter's own Apply reaches Application.runInBackground.
        /// </summary>
        public static void Enforce() => LongEventHandler.ExecuteWhenFinished(() =>
        {
            if (Prefs.RunInBackground) return;
            Prefs.RunInBackground = true; // forced true above; the log line is the point
            Prefs.Save();
            Log.Message("[SlopWorld] run in background turned on: the board has to keep up");
        });
    }
}
