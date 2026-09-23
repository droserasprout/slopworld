using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Retain camera, Accept, Cancel, and mod key bindings.
    // Keep all definitions because KeyPrefs and other readers reference them.
    // KeyBindingsPage omits disabled bindings, and NotBound suppresses their input. WindowStack requires Cancel to close windows.
    public static class StripKeys
    {
        static readonly string[] CameraDolly =
        {
            "MapDolly_Up", "MapDolly_Down", "MapDolly_Left", "MapDolly_Right",
        };

        static readonly KeyPrefs.BindingSlot[] CameraSlots =
        {
            KeyPrefs.BindingSlot.A, KeyPrefs.BindingSlot.B,
        };

        static readonly HashSet<KeyCode> FreedCameraKeys = new HashSet<KeyCode>
        {
            KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D,
        };

        static readonly HashSet<string> Keep = new HashSet<string>
        {
            "MapDolly_Up", "MapDolly_Down", "MapDolly_Left", "MapDolly_Right",
            "MapZoom_In", "MapZoom_Out",
            "Accept", "Cancel",
        };

        // Identify mod bindings by content pack so new definitions require no additional list entry.
        public static bool Kept(KeyBindingDef def) =>
            def != null
            && (Keep.Contains(def.defName)
                || (ModEntry.Instance != null
                    && def.modContentPack == ModEntry.Instance.Content));

        // Patch the four input state getters. Leave MainKey available for labels.
        static readonly string[] Reads =
        {
            "KeyDownEvent", "IsDownEvent", "JustPressed", "IsDown",
        };

        public static void Apply(Harmony h)
        {
            var pre = new HarmonyMethod(
                AccessTools.Method(typeof(StripKeys), nameof(NotBound)));
            foreach (var read in Reads)
                h.Patch(AccessTools.PropertyGetter(typeof(KeyBindingDef), read), prefix: pre);

            h.Patch(AccessTools.Method(typeof(KeyPrefs), nameof(KeyPrefs.Init)),
                postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(StripKeys), nameof(AfterKeyPrefsInit))));
            h.Patch(AccessTools.Method(typeof(KeyPrefsData), nameof(KeyPrefsData.ResetToDefaults)),
                postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(StripKeys), nameof(AfterKeyPrefsReset))));

            // Remove conflicting camera keys now in case preferences have already loaded.
            // Repeat after initialization and reset so saved preferences and defaults use the same rules.
            SanitizeCameraKeys(KeyPrefs.KeyPrefsData);
        }

        static void AfterKeyPrefsInit() => SanitizeCameraKeys(KeyPrefs.KeyPrefsData);

        static void AfterKeyPrefsReset(KeyPrefsData __instance) => SanitizeCameraKeys(__instance);

        static void SanitizeCameraKeys(KeyPrefsData data)
        {
            if (data == null) return;

            bool changed = false;
            foreach (string name in CameraDolly)
            {
                var def = DefDatabase<KeyBindingDef>.GetNamedSilentFail(name);
                if (def == null) continue;

                foreach (var slot in CameraSlots)
                {
                    if (!FreedCameraKeys.Contains(data.GetBoundKeyCode(def, slot))) continue;
                    changed |= data.SetBinding(def, slot, KeyCode.None);
                }
            }

            if (changed && ReferenceEquals(KeyPrefs.KeyPrefsData, data)) KeyPrefs.Save();
        }

        // Suppress input for hidden bindings because ScreenshotTaker and MainButtonsOnGUI still read them.
        static bool NotBound(KeyBindingDef __instance, ref bool __result)
        {
            if (Kept(__instance)) return true;
            __result = false;
            return false;
        }
    }
}
