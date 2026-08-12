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
            // Drop dev mode; debug tools are not part of this sim, but keep General's other rows.
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

        // Gate on the actual content view; a global flag could remain set after an exception and
        // filter unrelated listings.
        static bool Drops(string label) =>
            label != null
            && OptionsView.Anywhere
            && Dropped.Any(built => label == built());

        // Mark categories as dev instead of removing defs: vanilla hides them while preserving
        // database identity and row indexing, and dev mode restores them.
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
