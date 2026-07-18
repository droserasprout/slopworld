using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // With the mod loaded RimWorld is a viewer, not a colony you play, so most of
    // its own UI is dead weight. These patches strip it wholesale and
    // unconditionally: there is no setting, being loaded is the switch.
    //
    // Kept on purpose (agent-facing or navigation): the colonist bar, the inspect
    // pane, the Menu main button (save / load / quit) and our own Agents button.
    // Everything else in the bottom bar, both notification stacks, the alert
    // readout, the resource readout, the global controls (date / speed / temp) and
    // the mouseover readout are suppressed. See DROPS.md for the full list.
    //
    // Each patch prefixes an OnGUI method and returns false to skip its draw. The
    // target names are the 1.6 ones (this mod is 1.6-only).

    /// <summary>Message toasts, top-left.</summary>
    [HarmonyPatch(typeof(Messages), "MessagesDoGUI")]
    public static class Patch_Hide_Messages
    {
        static bool Prefix() => false;
    }

    /// <summary>The letter stack, right edge.</summary>
    [HarmonyPatch(typeof(LetterStack), "LettersOnGUI")]
    public static class Patch_Hide_Letters
    {
        static bool Prefix() => false;
    }

    /// <summary>The alert list, right edge.</summary>
    [HarmonyPatch(typeof(AlertsReadout), "AlertsReadoutOnGUI")]
    public static class Patch_Hide_Alerts
    {
        static bool Prefix() => false;
    }

    /// <summary>Colony resource counts, bottom-left.</summary>
    [HarmonyPatch(typeof(ResourceReadout), "ResourceReadoutOnGUI")]
    public static class Patch_Hide_Resources
    {
        static bool Prefix() => false;
    }

    /// <summary>Date, temperature, season and the speed / time buttons, bottom-right.</summary>
    [HarmonyPatch(typeof(GlobalControls), "GlobalControlsOnGUI")]
    public static class Patch_Hide_GlobalControls
    {
        static bool Prefix() => false;
    }

    /// <summary>Terrain / thing-under-cursor readout, bottom-left.</summary>
    [HarmonyPatch(typeof(MouseoverReadout), "MouseoverReadoutOnGUI")]
    public static class Patch_Hide_Mouseover
    {
        static bool Prefix() => false;
    }

    /// <summary>The tutorial "learning helper" concept panel, top-right.</summary>
    [HarmonyPatch(typeof(LearningReadout), "LearningReadoutOnGUI")]
    public static class Patch_Hide_Learning
    {
        static bool Prefix() => false;
    }

    /// <summary>
    /// The inspect pane's top-right buttons - Info card, hostility response and
    /// rename - none of which apply to an agent avatar. lineEndWidth is an
    /// accumulator the label sizing reads back, so zero it: with no buttons drawn,
    /// the name gets the pane's full width.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Inspect), "DoInspectPaneButtons")]
    public static class Patch_Hide_InspectButtons
    {
        static bool Prefix(ref float lineEndWidth)
        {
            lineEndWidth = 0f;
            return false;
        }
    }

    /// <summary>
    /// The pawn overview in the inspect pane: the Health / Food / Mood bars, the
    /// timetable and area selectors, and the inspect line that leads with gender
    /// and age. All colony-management detail, useless for a viewer, so the whole
    /// fill is skipped for pawns. Non-pawn selections (zones, storage, buildings)
    /// still draw normally.
    /// </summary>
    [HarmonyPatch(typeof(InspectPaneFiller), "DoPaneContentsFor")]
    public static class Patch_Hide_InspectContents
    {
        static bool Prefix(ISelectable sel) => !(sel is Pawn);
    }

    /// <summary>
    /// Hides every bottom-bar button except Menu, Inspect and our Agents tab.
    /// MainButtonWorker.Visible is virtual and overridden by several workers
    /// (World, Quests, Mechs...), so we postfix the base getter plus every
    /// declared override. Patched manually from the bootstrap because the target
    /// set is discovered by reflection.
    /// </summary>
    public static class Patch_MainButtons
    {
        // Inspect is kept because it backs the inspect pane; Menu for save / quit.
        static readonly HashSet<string> Keep = new HashSet<string>
        {
            "Menu", "Inspect", "SlopWorld_Agents",
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
        }

        static void KeepOnly(MainButtonWorker __instance, ref bool __result)
        {
            if (!__result) return;
            if (Keep.Contains(__instance.def?.defName)) return;
            __result = false;
        }
    }

    /// <summary>
    /// Hides the pawn inspect-pane tabs that only make sense when you play the
    /// colony: Bio, Needs, Health, Gear and Social. The pane itself stays for the
    /// summary line, and non-pawn tabs (and any we don't name) are untouched.
    ///
    /// InspectTabBase.IsVisible is virtual: Health inherits the base getter while
    /// the others override it and don't chain up, so - like Patch_MainButtons - we
    /// postfix the base getter plus each override and force it false by type.
    /// Patched manually from the bootstrap.
    /// </summary>
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

            // The overriders compute their own visibility and may not call base,
            // so each declared getter needs the postfix too.
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
