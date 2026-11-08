using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // One owner spans startup, scene changes and normal full-screen loads. Bootstrap
    // readiness is not a drawing boundary; menu/play frames own the end of a text stream.
    internal static class LoadingScreenHost
    {
        internal static bool HasPainted { get; private set; }
        static readonly FieldInfo CurrentEvent = AccessTools.Field(
            typeof(LongEventHandler),
            "currentEvent"
        );
        static readonly FieldInfo ForceHide = AccessTools.Field(
            CurrentEvent?.FieldType,
            "forceHideUI"
        );
        static readonly FieldInfo Displayed = AccessTools.Field(
            CurrentEvent?.FieldType,
            "alreadyDisplayed"
        );
        static readonly MethodInfo UseStandardWindow = AccessTools.PropertyGetter(
            CurrentEvent?.FieldType,
            "UseStandardWindow"
        );

        internal static MethodBase[] InteractiveTargets() =>
            new MethodBase[]
            {
                AccessTools.Method(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI)),
                AccessTools.Method(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI)),
            };

        internal static MethodBase WaitTarget() =>
            AccessTools.PropertyGetter(
                typeof(LongEventHandler),
                nameof(LongEventHandler.ShouldWaitForEvent)
            );

        internal static void Validate()
        {
            if (
                CurrentEvent == null
                || ForceHide?.FieldType != typeof(bool)
                || Displayed?.FieldType != typeof(bool)
                || UseStandardWindow?.ReturnType != typeof(bool)
            )
                throw new MissingMemberException("Long-event background/repaint contract changed.");
        }

        internal static void Install(Harmony harmony)
        {
            Validate();
            harmony.Patch(
                AccessTools.Method(
                    typeof(LongEventHandler),
                    nameof(LongEventHandler.LongEventsOnGUI)
                ),
                prefix: new HarmonyMethod(typeof(LoadingScreenHost), nameof(Draw))
                {
                    priority = Priority.First,
                }
            );
            foreach (var target in InteractiveTargets())
                harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(typeof(LoadingScreenHost), nameof(InteractiveFrame))
                );
            harmony.Patch(
                WaitTarget(),
                postfix: new HarmonyMethod(typeof(LoadingScreenHost), nameof(WaitForFullScreen))
            );
        }

        static void WaitForFullScreen(ref bool __result)
        {
            // Root must not paint menu/map UI over a synchronous event that we promoted
            // from a small window into the ongoing full-screen loading session.
            __result |= LongEventHandler.AnyEventNowOrWaiting && Patch_LoadingTips.IsFullScreen;
        }

        static void InteractiveFrame()
        {
            if (Event.current.type == EventType.Repaint)
                Patch_LoadingTips.InteractiveFrame(LongEventHandler.AnyEventNowOrWaiting);
        }

        static bool Draw()
        {
            object current = CurrentEvent.GetValue(null);
            if (current == null)
                return true;
            if ((bool)ForceHide.GetValue(current))
                return false;
            Patch_LoadingTips.Begin(current);
            // Preserve small in-game operation windows. Entry/startup events always use
            // our full-screen renderer, even when vanilla omits extra UI or requests a bubble.
            bool canUseGameWindow =
                Current.ProgramState == ProgramState.Playing
                && Find.UIRoot != null
                && Find.WindowStack != null
                && (bool)UseStandardWindow.Invoke(current, null);
            if (!Patch_LoadingTips.DrawFullScreen(canUseGameWindow))
                return true;
            UIMenuBackgroundManager.background ??= new UI_BackgroundMain();
            UIMenuBackgroundManager.background.BackgroundOnGUI();
            if (Event.current.type == EventType.Repaint)
            {
                Patch_LoadingTips.DrawPanel();
                HasPainted = true;
            }
            // Synchronous events wait for their first repaint. Suppressing the status
            // window must retain this acknowledgement or startup can deadlock.
            if (Event.current.type == EventType.Repaint)
                Displayed.SetValue(current, true);
            return false;
        }
    }
}
