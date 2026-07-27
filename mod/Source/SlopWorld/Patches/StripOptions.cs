using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The Options window, cut down to the questions this build still answers.
    ///
    /// It is the one vanilla window left standing - the mod's own settings live
    /// in it, so it cannot simply go the way the rest of the chrome did - and
    /// what is in it is still written for a colony sim. Two kinds of row are
    /// wrong here rather than merely unused: one whose answer this build has
    /// already made for you, and one about a game that is not running.
    ///
    /// A category is taken out whole; a row is taken out one at a time. The two
    /// are different jobs because <c>Dialog_Options</c> is built two different
    /// ways. Categories are <see cref="OptionCategoryDef"/>s and
    /// <c>DoWindowContents</c> walks the database, so there is a list to drop
    /// one from. Rows are not a list at all: <c>DoOptions</c> makes the
    /// <see cref="Listing_Standard"/> and hands it to a private method per
    /// category which draws its rows by calling widgets on it, one line of code
    /// each. So a row can only be taken out on the way past.
    /// </summary>
    public static class StripOptions
    {
        /// <summary>
        /// The finished labels this build will not draw, built rather than
        /// written down because that is the only thing a widget is handed that
        /// says which row it is - the call site translates the key and the
        /// prefix sees the result. A key with no translation comes back as
        /// itself, so this holds in any language including a missing one, the
        /// same rule the main menu's dropped rows go by.
        ///
        /// Two are plain keys and one is not: vanilla writes the count into the
        /// slider's own label ("Autosaves count: {0}"), so the label to match is
        /// the one the current preference would produce. Which is why these are
        /// thunks and not an array of strings - a table computed once at load
        /// would be a table holding the count this game started with.
        ///
        /// All three are General, and all three are already decided:
        /// <see cref="Patch_RunInBackground"/> forces its preference on, so that
        /// checkbox is a control that appears to do nothing, and
        /// <c>AutoSaver</c> writes the colony on the wall clock, so vanilla's
        /// interval and its count of files are settings for a saver that is not
        /// the one running.
        /// </summary>
        static readonly Func<string>[] Dropped =
        {
            () => "AutosaveInterval".Translate(),
            () => "AutosavesCount".Translate(Prefs.AutosavesCount),
            () => "RunInBackground".Translate(),
        };

        /// <summary>
        /// Whether the row being drawn is one of them. Gated on the window
        /// rather than on a flag armed around <c>DoOptions</c>: these widgets are
        /// drawn all over the game and by this mod's own dialogs, and a flag left
        /// set by an exception thrown mid-listing would filter every listing
        /// after it for the life of the process. <c>currentlyDrawnWindow</c>
        /// cannot be stranded, and it is true for exactly as long as the answer
        /// should be.
        /// </summary>
        static bool Drops(string label) =>
            label != null
            && Find.WindowStack?.currentlyDrawnWindow is Dialog_Options
            && Dropped.Any(built => label == built());

        /// <summary>
        /// Takes the Gameplay category off the left-hand column. It is the one
        /// page in there that is entirely about the game this product tore out -
        /// storyteller, settlement count, pause on load, the learning helper,
        /// the names you want to see - so there is nothing in it to keep and no
        /// point drawing the row that opens it.
        ///
        /// Done by setting the def's own <c>isDev</c> rather than by patching
        /// anything, because that is the switch vanilla already reads: the loop
        /// in <c>DoWindowContents</c> skips a dev category unless
        /// <c>Prefs.DevMode</c>, and it advances its row counter only for the
        /// ones it draws, so a skipped category leaves no hole in the column.
        /// The def stays in the database, which is the point of moving a field
        /// rather than removing it - <see cref="OptionCategoryDefOf"/> names all
        /// eight, and a def taken out from under it is an error at every rebind.
        ///
        /// The cost is that dev mode brings it back. That is the honest reading:
        /// the same switch un-hides vanilla's own Dev page and the debug
        /// toolbar, so somebody who has thrown it is somebody who has asked to
        /// see the machinery.
        /// </summary>
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

        /// <summary>
        /// Run in background. Void, so declining to call it is the whole of it:
        /// the listing advances inside the widget rather than before it, so a row
        /// that is never drawn leaves no gap where it would have been. The
        /// preference is read into a local above the call and written back below
        /// it either way, which with the checkbox skipped is a round trip.
        /// </summary>
        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.CheckboxLabeled),
            new[] { typeof(string), typeof(bool), typeof(string), typeof(float), typeof(float) },
            new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal,
                    ArgumentType.Normal, ArgumentType.Normal })]
        public static class Patch_OptionCheckbox
        {
            static bool Prefix(string label) => !Drops(label);
        }

        /// <summary>
        /// Autosave interval. Answers false, which is what the button says when
        /// it has not been clicked, so the float menu of intervals never opens.
        /// </summary>
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

        /// <summary>
        /// Autosaves count. Hands back the value it was given, because the caller
        /// assigns the answer straight into the preference - anything else here
        /// would be this patch quietly editing the setting it is hiding.
        /// </summary>
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
