using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Block map selection and camera input separately from MapInterface clicks.
    // Retain selector UI and gizmos for agents selected through the sidebar.
    static class EcoMapInput
    {
        // CameraJumper bypasses the CameraDriver motion check.
        // Select the agent without moving the camera while Eco is resting.
        public static void SelectAgent(Pawn pawn)
        {
            if (pawn == null) return;
            if (Eco.Resting)
            {
                Find.Selector.Select(pawn);
                return;
            }

            CameraJumper.TryJumpAndSelect(pawn);
        }

        // Cancel the drag rectangle before drawing because Selector handles input later.
        // This also covers the first event after enabling Eco.
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
                // Cancel active gestures without clearing the selected agent or its action buttons.
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

        // Edge scrolling runs before the base game motion check.
        // Clear pending drag input and camera velocity so movement does not continue after Eco ends.
        // Keep CameraDriver.Update active for projection and resolution changes.
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
