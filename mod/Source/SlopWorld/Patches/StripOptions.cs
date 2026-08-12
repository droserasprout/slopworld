using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // The one vanilla window left standing, the mod's own settings being in it. Categories
    // are taken out whole and rows one at a time, because Dialog_Options is built two ways:
    // categories are OptionCategoryDefs walked out of the database, where rows are widget
    // calls a private method per category makes on one Listing_Standard.
    public static class StripOptions
    {
        // Built rather than written down: the finished label is the only thing a widget is
        // handed that says which row it is, and a key with no translation comes back as
        // itself. One is not a plain key - vanilla writes the count into the slider's own
        // label, so a table computed at load would hold the count this game started with.
        static readonly Func<string>[] Dropped =
        {
            () => "AutosaveInterval".Translate(),
            () => "AutosavesCount".Translate(Prefs.AutosavesCount),
            () => "RunInBackground".Translate(),
            // Dev mode is not a thing this game offers. Every developer key binding is
            // gone with the rest of the keyboard (see StripKeys), the dev palette and the
            // debug menus are vanilla's own tools for a sim that is not ticking, and the
            // honest way to try something is the unmodded game. The row is dropped rather
            // than the whole category, so everything else General holds (language,
            // resolution, volumes) is still somewhere the player can reach.
            () => "DevelopmentMode".Translate(),
            // The "Modify" button on Controls, which opens vanilla's Dialog_KeyBindings on
            // the full list of defs - the dropped ones included. The Shortcuts page is the
            // one door now; the rest of Controls is the camera's, so the category stays.
            () => "KeyboardConfig".Translate(),
            // Interface's UI scale row, a float menu over a fixed ladder with the rungs its
            // own guard rejects left out. Appearance has the slider, over the whole range
            // (see SlopUIScale); two doors to one pref, disagreeing about its span, is one
            // door too many.
            () => "UIScale".Translate(),
        };

        // Gated on who is drawing rather than on a flag armed around DoOptions: these widgets
        // are drawn all over the game, and a flag stranded by an exception mid-listing would
        // filter every listing after it for the life of the process. `OptionsView.Anywhere` is
        // that question now - the pages are content in the chrome's window, so the window being
        // drawn is no longer the dialog's.
        static bool Drops(string label) =>
            label != null
            && OptionsView.Anywhere
            && Dropped.Any(built => label == built());

        // By each def's own isDev rather than by patching, because that is the switch vanilla
        // already reads: the loop skips a dev category unless Prefs.DevMode and advances its
        // row counter only for the ones it draws, so the column has no hole. The defs stay in
        // the database - OptionCategoryDefOf names all eight. Dev mode brings them back.
        //
        // Not by removing the def from `AllDefsListForReading`: that list *is* the database's
        // own, so a def taken out of it is gone from every `AllDefs` walk in the process while
        // `GetNamed` still answers for it, and any mod that enumerates the categories to hang
        // its own row off one finds a hole.
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

        // Void, so declining to call it is the whole of it: the listing advances inside
        // the widget rather than before it, so a row never drawn leaves no gap.
        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.CheckboxLabeled),
            new[] { typeof(string), typeof(bool), typeof(string), typeof(float), typeof(float) },
            new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal,
                    ArgumentType.Normal, ArgumentType.Normal })]
        public static class Patch_OptionCheckbox
        {
            static bool Prefix(string label) => !Drops(label);
        }

        // Answers false, which is what the button says when it has not been clicked.
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

        // Hands back the value it was given, because the caller assigns the answer
        // straight into the preference.
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
