using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // MapInterface's click gate does not cover Selector or CameraDriver. Leave the
    // selector's UI pass and gizmo grid alive for sidebar-selected agents.
    static class EcoMapInput
    {
        // The rectangle draws before Selector handles input, including the first event
        // after Eco is enabled. Cancel it at that earlier draw entry as well.
        [HarmonyPatch(typeof(DragBox), nameof(DragBox.DragBoxOnGUI))]
        static class SelectionRectangle
        {
            static bool Prefix(DragBox __instance)
            {
                if (!Eco.Resting) return true;
                __instance.active = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Selector), "HandleMapClicks")]
        static class Selection
        {
            static bool Prefix(Selector __instance)
            {
                if (!Eco.Resting) return true;
                // Cancel gestures already in progress when Eco was enabled, without
                // clearing the selected agent or its action buttons.
                __instance.Notify_DialogOpened();
                return false;
            }
        }

        [HarmonyPatch(typeof(CameraDriver), "AnythingPreventsCameraMotion", MethodType.Getter)]
        static class CameraMotion
        {
            static void Postfix(ref bool __result)
            {
                if (Eco.Resting) __result = true;
            }
        }

        // Edge scrolling is sampled before vanilla checks its motion gate. Clear pending
        // drag input and inertia too, so neither moves the camera or survives leaving Eco.
        // Keep CameraDriver.Update running for projection and resolution changes.
        [HarmonyPatch(typeof(CameraDriver), "CalculateCurInputDollyVect")]
        static class CameraDolly
        {
            static bool Prefix(ref Vector2 __result, ref Vector3 ___velocity,
                ref Vector2 ___desiredDolly, ref Vector2 ___desiredDollyRaw,
                List<CameraDriver.DragTimeStamp> ___dragTimeStamps)
            {
                if (!Eco.Resting) return true;
                ___velocity = Vector3.zero;
                ___desiredDolly = ___desiredDollyRaw = Vector2.zero;
                ___dragTimeStamps.Clear();
                __result = Vector2.zero;
                return false;
            }
        }
    }
}
