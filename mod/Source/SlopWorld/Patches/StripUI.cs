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

    /// <summary>
    /// OnGUI methods drawing colony-management chrome we always suppress. Each is
    /// prefixed with a shared "return false" to skip its draw wholesale. A table +
    /// manual patch instead of one attribute class apiece: the target set is just
    /// data. Patched from the bootstrap. 1.6 names; this mod is 1.6-only.
    /// </summary>
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
    /// The "select next thing in this cell" overlay button. It's drawn in
    /// InspectPaneOnGUI, separate from the pane buttons above, and gated on this
    /// getter. Only colonists are selectable now, so cycling a cell's things is
    /// moot; force the gate false to drop the button.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Inspect), "ShouldShowSelectNextInCellButton", MethodType.Getter)]
    public static class Patch_Hide_SelectNextInCell
    {
        static void Postfix(ref bool __result) => __result = false;
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
