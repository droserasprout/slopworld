using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Hide base game UI elements while retaining colonist and terminal controls. Use RimWorld 1.6 patch targets.

    // Apply one shared prefix to the target table.
    public static class Patch_HideGui
    {
        static readonly (Type Type, string Method)[] Targets =
        {
            (typeof(Messages), nameof(Messages.MessagesDoGUI)),                 // message toasts, top-left
            (typeof(LetterStack), nameof(LetterStack.LettersOnGUI)),               // letter stack, right edge
            (typeof(AlertsReadout), nameof(AlertsReadout.AlertsReadoutOnGUI)),       // alert list, right edge
            (typeof(ResourceReadout), nameof(ResourceReadout.ResourceReadoutOnGUI)),   // resource counts, bottom-left
            (typeof(GlobalControls), nameof(GlobalControls.GlobalControlsOnGUI)),     // date/temp/speed, bottom-right
            (typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI)), // under-cursor readout, bottom-left
            (typeof(LearningReadout), nameof(LearningReadout.LearningReadoutOnGUI)),   // tutorial concept panel, top-right
        };

        public static void Apply(Harmony h)
        {
            var pre = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_HideGui), nameof(Skip)));
            foreach (var (type, method) in Targets)
                h.Patch(AccessTools.Method(type, method), prefix: pre);
        }

        static bool Skip() => false;
    }

    // Hide the cell inspector and its Alt-key brackets through their shared ShouldShow check.
    [HarmonyPatch(typeof(CellInspectorDrawer), "ShouldShow")]
    public static class Patch_Hide_CellInspector
    {
        static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

    // Hide the separate beauty display that also uses the Alt key.
    [HarmonyPatch(typeof(BeautyDrawer), "ShouldShow")]
    public static class Patch_Hide_Beauty
    {
        static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

    // Patch before map ticks to prevent forbidden overlay handles from enabling.
    [HarmonyPatch(typeof(CompForbiddable), "UpdateOverlayHandle")]
    public static class Patch_Hide_ForbiddenOverlay
    {
        static bool Prefix() => false;
    }

    // Hide information, hostility, and rename buttons.
    // Set lineEndWidth to zero so the name can use the full pane width.
    [HarmonyPatch(typeof(MainTabWindow_Inspect), "DoInspectPaneButtons")]
    public static class Patch_Hide_InspectButtons
    {
        static bool Prefix(ref float lineEndWidth)
        {
            lineEndWidth = 0f;
            return false;
        }
    }

    // Agent selections hide the entire inspect window in InspectPanePatch.
    // Hide pawn content here too; retain normal drawing for non-pawn selections.
    [HarmonyPatch(typeof(InspectPaneFiller), nameof(InspectPaneFiller.DoPaneContentsFor))]
    public static class Patch_Hide_InspectContents
    {
        static bool Prefix(ISelectable sel) => !(sel is Pawn);
    }

    // Hide cell selection cycling because only colonists are selectable.
    [HarmonyPatch(typeof(MainTabWindow_Inspect), "ShouldShowSelectNextInCellButton", MethodType.Getter)]
    public static class Patch_Hide_SelectNextInCell
    {
        static void Postfix(ref bool __result) => __result = false;
    }

    // Hide the colonist bar during cutscenes or when ColonistBarStrip suppresses it.
    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    public static class Patch_Hide_ColonistBar
    {
        static bool Prefix() => !Cutscene.Playing && !ColonistBarStrip.Suppressed;
    }

    // Patch MainButtonWorker.Visible and its overrides. Also block activation so hidden buttons cannot respond to shortcuts.
    public static class Patch_MainButtons
    {
        // List every button that the mod permits. Omitted buttons remain hidden.
        static readonly HashSet<string> Keep = new HashSet<string>
        {
            "Menu", "Inspect",
            "SlopWorld_Projects", "SlopWorld_Agents", "SlopWorld_Library",
            "SlopWorld_Config",
        };

        public static void Apply(Harmony h)
        {
            var post = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_MainButtons), nameof(KeepOnly)));

            h.Patch(AccessTools.PropertyGetter(typeof(MainButtonWorker), nameof(MainButtonWorker.Visible)), postfix: post);
            foreach (var t in typeof(MainButtonWorker).AllSubclassesNonAbstract())
            {
                var g = AccessTools.DeclaredPropertyGetter(t, nameof(MainButtonWorker.Visible));
                if (g != null) h.Patch(g, postfix: post);
            }

            h.Patch(
                AccessTools.Method(typeof(MainButtonWorker),
                    nameof(MainButtonWorker.InterfaceTryActivate)),
                prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(Patch_MainButtons), nameof(OnlyIfShown))));
        }

        static void KeepOnly(MainButtonWorker __instance, ref bool __result)
        {
            if (!__result) return;
            if (Cutscene.Playing) { __result = false; return; } // a scene has the board
            if (Keep.Contains(__instance.def?.defName)) return;
            __result = false;
        }

        // Callers consume the key or click event before this check.
        static bool OnlyIfShown(MainButtonWorker __instance) => __instance.Visible;
    }

    // Patch the base visibility getter and each override because overrides can omit the base call.
    // Hide the listed tab types.
    public static class Patch_InspectTabs
    {
        static readonly HashSet<Type> Drop = new HashSet<Type>
        {
            typeof(ITab_Pawn_Character), // "Bio"
            typeof(ITab_Pawn_Needs),
            typeof(ITab_Pawn_Health),
            typeof(ITab_Pawn_Gear),
            typeof(ITab_Pawn_Social),
        };

        public static void Apply(Harmony h)
        {
            var post = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_InspectTabs), nameof(Hide)));

            // Patch the base getter for tabs that inherit it, including Health.
            h.Patch(AccessTools.PropertyGetter(typeof(InspectTabBase), nameof(InspectTabBase.IsVisible)), postfix: post);

            // Patch overrides that calculate visibility independently.
            foreach (var t in Drop)
            {
                var g = AccessTools.DeclaredPropertyGetter(t, nameof(InspectTabBase.IsVisible));
                if (g != null) h.Patch(g, postfix: post);
            }
        }

        static void Hide(InspectTabBase __instance, ref bool __result)
        {
            if (!__result) return;
            if (Drop.Contains(__instance.GetType())) __result = false;
        }
    }

    // Hide the bottom button bar. The sidebar menu provides these actions.
    [HarmonyPatch(typeof(MainButtonsRoot), nameof(MainButtonsRoot.MainButtonsOnGUI))]
    public static class Patch_Hide_BottomPanel
    {
        static bool Prefix() => false;
    }

    // Disable the main menu shortcut when Escape has no active window to close.
    [HarmonyPatch(typeof(UIRoot_Play), "OpenMainMenuShortcut")]
    public static class Patch_NoEscMenu
    {
        static bool Prefix() => false;
    }
}
