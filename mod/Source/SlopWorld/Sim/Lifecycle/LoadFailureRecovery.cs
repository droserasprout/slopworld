using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Own the failed-load handoff from the play scene to a new colony. Keep the
    // native loader/scene cleanup, but replace its delayed error and menu controls.
    internal static class LoadFailureRecovery
    {
        internal static bool Pending { get; private set; }
        static Window _notice;

        internal static void Begin(Exception error)
        {
            Log.Error($"[SlopWorld] colony load failed: {error}");
            if (Pending) return;
            // Arm before GoToMainMenu: its save prefix must not save a partial game.
            Pending = true;
            _notice = null;
            Scribe.ForceStop();
            GenScene.GoToMainMenu();
        }

        internal static void ShowPendingNotice()
        {
            // Scene transitions replace the window stack. Deliver only after the
            // entry scene is idle, from Root.Update rather than the loading thread.
            if (!Pending || LongEventHandler.AnyEventNowOrWaiting || !GenScene.InEntryScene) return;
            var stack = Find.WindowStack;
            if (stack == null || (_notice != null && stack.IsOpen(_notice))) return;

            _notice = AlertDialog.Create("Could not load colony",
                "SlopWorld could not load the saved colony. The save may be damaged.\n\n" +
                "Your configuration, local agent data, and daemon are unaffected. " +
                "The colony is purely cosmetic.\n\n" +
                "Start a new colony to continue. Existing saves have been kept. " +
                "Details are in the game log.",
                "New colony", StartNewColony);
            _notice.closeOnCancel = false;
            stack.Add(_notice);
        }

        static void StartNewColony()
        {
            if (!Pending) return;
            Pending = false;
            _notice = null;
            QuickStart.Queue();
        }
    }

    [HarmonyPatch(typeof(GameAndMapInitExceptionHandlers),
        nameof(GameAndMapInitExceptionHandlers.ErrorWhileLoadingGame))]
    internal static class Patch_LoadFailureRecovery
    {
        static bool Prefix(Exception e)
        {
            LoadFailureRecovery.Begin(e);
            return false;
        }
    }

    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class Patch_LoadFailureNotice
    {
        static void Postfix() => LoadFailureRecovery.ShowPendingNotice();
    }

    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    internal static class Patch_LoadFailureMenu
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix() => !LoadFailureRecovery.Pending;
    }
}
