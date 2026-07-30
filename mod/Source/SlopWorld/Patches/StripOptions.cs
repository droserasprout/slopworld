using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // The one vanilla window left standing, the mod's own settings being in it. A category is
    // taken out whole and a row one at a time, because Dialog_Options is built two ways:
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
        };

        // Gated on the window rather than a flag armed around DoOptions: these widgets are
        // drawn all over the game, and a flag stranded by an exception mid-listing would
        // filter every listing after it for the life of the process.
        static bool Drops(string label) =>
            label != null
            && Find.WindowStack?.currentlyDrawnWindow is Dialog_Options
            && Dropped.Any(built => label == built());

        // By the def's own isDev rather than by patching, because that is the switch vanilla
        // already reads: the loop skips a dev category unless Prefs.DevMode and advances its
        // row counter only for the ones it draws, so the column has no hole. The def stays in
        // the database - OptionCategoryDefOf names all eight. Dev mode brings it back.
        public static void Hide()
        {
            var gone = OptionCategoryDefOf.Gameplay;
            if (gone == null)
            {
                Log.Warning("[SlopWorld] no Gameplay option category to hide");
                return;
            }
            gone.isDev = true;
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
