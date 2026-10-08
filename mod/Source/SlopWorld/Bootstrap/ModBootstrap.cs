using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    [StaticConstructorOnStartup]
    public static class ModBootstrap
    {
        static ModBootstrap()
        {
            // Check ModProfile before applying patches.
            // Activate the mod only in its marked save data folder.
            if (!ModProfile.Ok)
            {
                ProfileRefusal.Install();
                ModProfile.Complain();
                return;
            }

            UiClipboard.Provider = new DaemonUiClipboard();
            RowChrome.OverlayHitTest = ColonistBarStrip.SidebarHover;

            var h = new Harmony("io.drsr.slopworld");
            try
            {
                h.PatchAll(Assembly.GetExecutingAssembly());
                Patch_HideGui.Apply(h);
                Patch_MainButtons.Apply(h);
                StripKeys.Apply(h);
                Patch_InspectTabs.Apply(h);
                Patch_NoRelateAgents.Apply(h);
            }
            catch (Exception e)
            {
                // PatchAll stops at the first failure.
                // Log the error and retain patches applied before the exception.
                Log.Error($"[SlopWorld] patching incomplete: {e}");
            }
            // Enable the required background-run preference if the saved setting disables it.
            Patch_RunInBackground.Enforce();
            // Change the existing definition to hide the base-game options category.
            StripOptions.Hide();
            // Add the replacement options category at runtime.
            // An inactive mod then leaves no empty options tab.
            ModOptions.Install();
            // Intercepts Alt+F4 / window close to save and show a confirmation dialog.
            QuitInterceptor.Register();
            Log.Message("[SlopWorld] Patched. Daemon at " + DaemonClient.BaseUrl);
            UiFont.Apply();
            TerminalLatencyFrame.Install();
        }
    }

    // Update the hub from Root.Update, which runs both in menus and during play.
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    public static class Patch_Root_Update
    {
        static void Prefix(out long __state) => __state = PerfTrace.Start();

        static void Postfix(long __state)
        {
            // The jukebox has no MonoBehaviour of its own.
            // Update it before the client so connection work cannot delay audio updates within this frame.
            Radio.Update();
            SessionHub.Instance.Update();
            WindowTitle.Follow();
            // Intercepts Alt+F4 / window close: shows the confirmation dialog on the
            // frame after the save completes.
            QuitInterceptor.Check();
            // Adjust the frame rate when the window loses focus.
            // Check each frame, including frames in menus.
            BackgroundFrames.Follow();
            LinuxGameWindow.Follow();
            ModEntry.Instance?.settings.FlushIfDue();
            DeadCursor.Tick();
            // Check clicks once per frame in Update instead of once per GUI event in OnGUI.
            // GetMouseButtonDown retains Unity's input flag even after HandleEventsHighPriority consumes the GUI event.
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                DeadCursor.Click();
        }

        static void Finalizer(long __state) => PerfTrace.End("root-update", __state, 1);
    }

    // Keep touchpad storms from making the options UI process thousands of full GUI events.
    [HarmonyPatch(typeof(Root), "OnGUI")]
    public static class Patch_Root_WheelQueue
    {
        static void Prefix()
        {
            AgentSidebar.ApplyPendingLayoutChange();
            var current = Event.current;
            if (current == null) return;
            var stack = Find.WindowStack;
            if (stack == null || (TerminalWindow.ShowingAs<OptionsView>() == null &&
                stack.WindowOfType<Dialog_Options>() == null &&
                stack.WindowOfType<UiMenu>() == null))
                return;
            WheelEventQueue.Compact(current);
        }
    }
}
