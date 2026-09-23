using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Filter base game option categories and individual rows.
    // Dialog_Options reads categories from definitions. Private category methods draw rows through Listing_Standard.
    public static class StripOptions
    {
        // Build translated labels when comparing rows. Widgets receive labels rather than translation keys.
        // AutosavesCount includes the current count, so its label cannot be cached at startup.
        static readonly Func<string>[] Dropped =
        {
            () => "AutosaveInterval".Translate(),
            () => "AutosavesCount".Translate(Prefs.AutosavesCount),
            () => "RunInBackground".Translate(),
            // Hide development mode while retaining other General rows.
            () => "DevelopmentMode".Translate(),
            // Hide the base game keyboard configuration. The Library page manages mod library items.
            () => "KeyboardConfig".Translate(),
            // Hide the fixed UI scale menu. Appearance provides a slider for the full supported range.
            () => "UIScale".Translate(),

            // Hide resolution selection and the borderless fullscreen notice.
            // Appearance provides the live fullscreen toggle.
            () => "Resolution".Translate(),
            () => "BorderlessFullscreen".Translate(),

            // Hide duplicate controls. General and Appearance provide text size and temperature settings.
            () => "DisableTinyText".Translate(),
            () => "TemperatureMode".Translate(),

            // Hide settings that no active system uses.

            // DeadCursor replaces both cursor activation paths. Appearance controls cursor selection.
            () => "CustomCursor".Translate(),

            // Hide clock settings because GlobalControls and the planet view are unavailable.
            () => "ShowRealtimeClock".Translate(),
            () => "TwelveHourClockMode".Translate(),

            // ColonistBarDrawPatch replaces the mood display. Agent pawns have no weapons to display under portraits.
            () => "VisibleMood".Translate(),
            () => "ShowWeaponsUnderPortrait".Translate(),

            // Gravships require quests and construction menus that this simulation does not provide.
            () => "GravshipCutscenes".Translate(),

            // Hide settings for unavailable world camera and designator controls.
            () => "ZoomSwitchLayer".Translate(),
            () => "RememberDrawStyle".Translate(),
        };

        // Filter only while the options content view exists.
        // A global flag could remain active after an exception and affect unrelated lists.
        static bool Drops(string label) =>
            label != null
            && OptionsView.Anywhere
            && Dropped.Any(built => label == built());

        // Mark categories as development categories to preserve definition identity and row indexing.
        // Development mode can show them again.
        public static void Hide()
        {
            Hide(OptionCategoryDefOf.General, "General");
            Hide(OptionCategoryDefOf.Gameplay, "Gameplay");
        }

        static void Hide(OptionCategoryDef category, string name)
        {
            if (category == null)
                Log.Warning($"[SlopWorld] no {name} option category to hide");
            else
                category.isDev = true;
        }

        // Skip the checkbox method before it advances the listing position so hidden rows leave no gap.
        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.CheckboxLabeled),
            new[] { typeof(string), typeof(bool), typeof(string), typeof(float), typeof(float) },
            new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal,
                    ArgumentType.Normal, ArgumentType.Normal })]
        public static class Patch_OptionCheckbox
        {
            static bool Prefix(string label) => !Drops(label);
        }

        // Return false for hidden buttons to indicate no click.
        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.ButtonTextLabeledPct))]
        public static class Patch_OptionButton
        {
            static bool Prefix(string label, ref bool __result)
            {
                if (!Drops(label)) return true;
                __result = false;
                return false;
            }
        }

        // Filter the borderless fullscreen notice separately because it uses a Label widget.
        // Return an empty rectangle without advancing the listing.
        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.Label),
            new[] { typeof(TaggedString), typeof(float), typeof(string) })]
        public static class Patch_OptionLabel
        {
            static bool Prefix(TaggedString label, ref Rect __result)
            {
                if (!Drops(label.ToString())) return true;
                __result = default(Rect);
                return false;
            }
        }

        // Return the original slider value because the caller assigns the result to the preference.
        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.SliderLabeled))]
        public static class Patch_OptionSlider
        {
            static bool Prefix(string label, float val, ref float __result)
            {
                if (!Drops(label)) return true;
                __result = val;
                return false;
            }
        }
    }
}
