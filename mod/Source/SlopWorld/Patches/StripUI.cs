using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // RimWorld is a viewer here, not a colony you play, so most of its UI is stripped
    // wholesale and unconditionally. Kept on purpose: the colonist bar, the inspect
    // pane, the Menu button and our own. Each patch prefixes an OnGUI method and
    // returns false. 1.6 names; this mod is 1.6-only.

    // The target set is data, so a table and a manual patch rather than one attribute
    // class apiece.
    public static class Patch_HideGui
    {
        static readonly (Type Type, string Method)[] Targets =
        {
            (typeof(Messages), "MessagesDoGUI"),                 // message toasts, top-left
            (typeof(LetterStack), "LettersOnGUI"),               // letter stack, right edge
            (typeof(AlertsReadout), "AlertsReadoutOnGUI"),       // alert list, right edge
            (typeof(ResourceReadout), "ResourceReadoutOnGUI"),   // resource counts, bottom-left
            (typeof(GlobalControls), "GlobalControlsOnGUI"),     // date/temp/speed, bottom-right
            (typeof(MouseoverReadout), "MouseoverReadoutOnGUI"), // under-cursor readout, bottom-left
            (typeof(LearningReadout), "LearningReadoutOnGUI"),   // tutorial concept panel, top-right
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

    // The cell inspector, shown while Alt is held. Both the overlay draw and the
    // Alt-hold bracket gate on ShouldShow, so forcing it false drops the whole thing.
    [HarmonyPatch(typeof(CellInspectorDrawer), "ShouldShow")]
    public static class Patch_Hide_CellInspector
    {
        static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

    // The beauty readout, on the same Alt key via CellInspectorDrawer.active but
    // drawn by BeautyDrawer, so it needs its own.
    [HarmonyPatch(typeof(BeautyDrawer), "ShouldShow")]
    public static class Patch_Hide_Beauty
    {
        static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

    // In 1.6 the forbidden overlay rides a persistent handle:
    // CompForbiddable.UpdateOverlayHandle enables it whenever the thing is forbidden,
    // and the mod loads before any map ticks, so skipping it means the handle is
    // never enabled.
    [HarmonyPatch(typeof(CompForbiddable), "UpdateOverlayHandle")]
    public static class Patch_Hide_ForbiddenOverlay
    {
        static bool Prefix() => false;
    }

    // Info card, hostility response and rename. lineEndWidth is an accumulator the
    // label sizing reads back, so zeroing it gives the name the pane's full width.
    [HarmonyPatch(typeof(MainTabWindow_Inspect), "DoInspectPaneButtons")]
    public static class Patch_Hide_InspectButtons
    {
        static bool Prefix(ref float lineEndWidth)
        {
            lineEndWidth = 0f;
            return false;
        }
    }

    // The Health/Food/Mood bars and the area selectors. An agent's colonist keeps the
    // inspect line alone; non-pawn selections still draw normally.
    [HarmonyPatch(typeof(InspectPaneFiller), "DoPaneContentsFor")]
    public static class Patch_Hide_InspectContents
    {
        static bool Prefix(ISelectable sel, Rect rect)
        {
            if (!(sel is Pawn pawn)) return true;
            if (AgentColony.Current?.SessionOf(pawn) == null) return false;

            // Vanilla draws this line in a group at the content origin, below the widget row
            // it just laid out; with no row, it starts at the top.
            Widgets.BeginGroup(rect);
            InspectPaneFiller.DrawInspectStringFor(sel, rect.AtZero());
            Widgets.EndGroup();
            return false;
        }
    }

    // Only colonists are selectable now, so cycling a cell's things is moot.
    [HarmonyPatch(typeof(MainTabWindow_Inspect), "ShouldShowSelectNextInCellButton", MethodType.Getter)]
    public static class Patch_Hide_SelectNextInCell
    {
        static void Postfix(ref bool __result) => __result = false;
    }

    // Hidden for the opening scene, and while a terminal is open - the bar is drawn
    // under every window and the terminal is opaque and fullscreen. The strip inside
    // the pane's title bar calls the same method from inside the window, and that call
    // is the one this lets through. See ColonistBarStrip.cs.
    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    public static class Patch_Hide_ColonistBar
    {
        static bool Prefix() => !Cutscene.Playing && !ColonistBarStrip.Suppressed;
    }

    // MainButtonWorker.Visible is virtual and overridden by several workers, so the
    // base getter and every declared override are postfixed, from the bootstrap
    // because the set is found by reflection.
    //
    // Hiding the button is not taking the tab away: with nothing selected,
    // right-clicking the map or pressing Tab put the Architect menu on a board that
    // builds nothing. MainButtonsRoot fires any def whose hotKey went down checking
    // only Disabled, and HandleLowPriorityShortcuts opens Architect by name - both
    // end at InterfaceTryActivate, which nothing overrides.
    public static class Patch_MainButtons
    {
        // Every button this mod ships has to be named here. `shortcuts` had a def, a
        // worker and an order from the day errands landed and drew nothing for want of a
        // line in this set: a button missing from here does not appear at all.
        static readonly HashSet<string> Keep = new HashSet<string>
        {
            "Menu", "Inspect",
            "SlopWorld_Projects", "SlopWorld_Agents", "SlopWorld_Shortcuts",
            "SlopWorld_Config",
        };

        public static void Apply(Harmony h)
        {
            var post = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_MainButtons), nameof(KeepOnly)));

            h.Patch(AccessTools.PropertyGetter(typeof(MainButtonWorker), "Visible"), postfix: post);
            foreach (var t in typeof(MainButtonWorker).AllSubclassesNonAbstract())
            {
                var g = AccessTools.DeclaredPropertyGetter(t, "Visible");
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

        // Both callers Use() the event before getting here, so the key and the click are
        // still swallowed - which is what we want.
        static bool OnlyIfShown(MainButtonWorker __instance) => __instance.Visible;
    }

    // Bio, Needs, Health, Gear and Social. IsVisible is virtual: Health inherits the
    // base getter while the others override it without chaining up, so the base and
    // each override are postfixed and forced false by type.
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

            // Base getter covers tabs that don't override IsVisible (e.g. Health).
            h.Patch(AccessTools.PropertyGetter(typeof(InspectTabBase), "IsVisible"), postfix: post);

            // The overriders compute their own visibility and may not call base.
            foreach (var t in Drop)
            {
                var g = AccessTools.DeclaredPropertyGetter(t, "IsVisible");
                if (g != null) h.Patch(g, postfix: post);
            }
        }

        static void Hide(InspectTabBase __instance, ref bool __result)
        {
            if (!__result) return;
            if (Drop.Contains(__instance.GetType())) __result = false;
        }
    }
}
