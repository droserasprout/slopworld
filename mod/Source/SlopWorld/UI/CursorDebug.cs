using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Draw after the root has painted everything so the marker can be compared with the
    // hardware cursor over both the menu and a live map. Event.mousePosition is already in
    // IMGUI's top-left coordinate space; Input.mousePosition is bottom-left screen space.
    public static class CursorDebugMarker
    {
        static int _drawnFrame = -1;

        public static void Draw()
        {
            if (!Settings.CursorDebug || Event.current == null
                || Event.current.type != EventType.Repaint)
                return;

            if (_drawnFrame == Time.frameCount) return;
            _drawnFrame = Time.frameCount;

            using (WidgetState.Save())
            {
                var p = Event.current.mousePosition;
                GUI.color = Color.green;
                GUI.DrawTexture(new Rect(Mathf.Floor(p.x), Mathf.Floor(p.y), 1f, 1f),
                    Texture2D.whiteTexture);
            }
        }
    }

    [HarmonyPatch(typeof(UIRoot_Entry), nameof(UIRoot_Entry.UIRootOnGUI))]
    public static class Patch_UIRoot_Entry_CursorDebug
    {
        static void Postfix() => CursorDebugMarker.Draw();
    }

    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    public static class Patch_UIRoot_Play_CursorDebug
    {
        static void Postfix() => CursorDebugMarker.Draw();
    }
}
