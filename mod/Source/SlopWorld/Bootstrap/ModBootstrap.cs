using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    [StaticConstructorOnStartup]
    public static class ModBootstrap
    {
        static ModBootstrap()
        {
            // First, and before anything is patched: this mod is only ever run in a save
            // folder of its own, and in anybody else's game it does nothing at all. See
            // ModProfile.
            if (!ModProfile.Ok)
            {
                ModProfile.Complain();
                return;
            }

            var h = new Harmony("drsr.slopworld");
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
                // PatchAll aborts on the first bad one, and a mod with no patches at all is
                // indistinguishable from a mod that isn't there. Log loud and keep whatever bound
                // before the throw.
                Log.Error($"[SlopWorld] patching incomplete: {e}");
            }
            // Not a patch: a preference this build insists on, which has to be put right once
            // for a file that has it off.
            Patch_RunInBackground.Enforce();
            // Nor is this one: a field moved on a def vanilla already reads, which is how a
            // whole options category goes.
            StripOptions.Hide();
            // And this is the category that arrives in its place, added to the database
            // rather than shipped as XML so a refusing mod leaves no empty tab behind.
            ModOptions.Install();
            // Intercepts Alt+F4 / window close to save and show a confirmation dialog.
            QuitInterceptor.Register();
            Log.Message("[SlopWorld] patched; daemon at " + DaemonClient.BaseUrl);
            UiFont.Apply();
        }
    }

    // Drives the hub. Root.Update runs on the menu and in-game alike.
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    public static class Patch_Root_Update
    {
        static void Prefix(out long __state) => __state = PerfTrace.Start();

        static void Postfix(long __state)
        {
            // The jukebox has no MonoBehaviour of its own. Run it before the client's
            // synchronous reconnect can block this frame while the daemon is restarting.
            Radio.Update();
            SessionHub.Instance.Update();
            WindowTitle.Follow();
            // Intercepts Alt+F4 / window close: shows the confirmation dialog on the
            // frame after the save completes.
            QuitInterceptor.Check();
            // Drops the framerate while the window is behind something else. Here because
            // it has to hold on the menu too, and because focus is a per-frame question.
            BackgroundFrames.Follow();
            WindowMaximizer.Follow();
            ModEntry.Instance?.settings.FlushIfDue();
            DeadCursor.Tick();
            // Update and not OnGUI, so it fires per frame rather than per event, and below
            // HandleEventsHighPriority, where the clicks that count are used -
            // GetMouseButtonDown still sees them, that flag being Input's own.
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                DeadCursor.Click();
        }

        static void Finalizer(long __state) => PerfTrace.End("root-update", __state, 1);
    }
}
