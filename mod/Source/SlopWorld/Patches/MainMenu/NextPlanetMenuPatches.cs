using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Modify the completed option list. DoMainMenuControls draws options and web links in separate passes.
    // Column identifies the first pass without depending on labels or layout. Remove the four translated options from that pass.
    [HarmonyPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))]
    public static class Patch_MenuOption
    {
        static readonly string[] Dropped =
            { "Save", "LoadGame", "ReviewScenario", "QuitToMainMenu" };

        public static bool Column;

        // Use the expected row count before the first menu display.
        // Update it after drawing the option list. The menu reads RequestedTabSize when it opens, not each frame.
        public static int RemovedRows = Dropped.Length;

        static void Prefix(List<ListableOption> optList)
        {
            bool first = Column;
            Column = false;

            if (!first) return; // the web links, drawn beside the options
            if (Current.ProgramState != ProgramState.Playing) return;
            if (NextPlanet.Leaving) return;
            if (!(Find.WindowStack?.currentlyDrawnWindow is MainTabWindow_Menu)) return;

            int gone = optList.RemoveAll(o => o != null && Dropped.Any(
                key => o.label == (string)key.Translate()));

            RemovedRows = gone;
        }
    }

    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.DoMainMenuControls))]
    public static class Patch_MenuFirstColumn
    {
        static void Prefix() => Patch_MenuOption.Column = true;
    }

    // Adjust the fixed menu height for removed rows. The menu has no scrolling.
    [HarmonyPatch(typeof(MainTabWindow_Menu), "RequestedTabSize", MethodType.Getter)]
    public static class Patch_MenuSize
    {
        // Use the minimum option height plus the space between options.
        const float RowH = 45f + 7f;

        static void Postfix(ref Vector2 __result) => __result.y -= RowH * Patch_MenuOption.RemovedRows;
    }

    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    public static class Patch_LandAgain
    {
        static void Prefix()
        {
            if (!NextPlanet.Pending) return;
            NextPlanet.CompletePendingLanding();
            Find.WindowStack.Add(new Page_SelectScenario());
        }
    }
}
