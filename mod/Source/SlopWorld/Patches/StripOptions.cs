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

            // The rest are rows this mod has already taken the last reader away from. A
            // preference nothing consults is worse than an absent one: it is a knob that
            // moves, and a player who moves it is owed the change it advertises.

            // The pointer is DeadCursor's, and both CustomCursor.Activate and Deactivate are
            // prefixed to lay ours down whichever way the pref went. Appearance picks it.
            () => "CustomCursor".Translate(),

            // Both clocks are drawn by GlobalControls, which Patch_HideGui declines, and by
            // the planet view, which has no button left to open it.
            () => "ShowRealtimeClock".Translate(),
            () => "TwelveHourClockMode".Translate(),

            // The mood bar under the portrait belongs to ColonistBarColonistDrawer.DrawColonist,
            // which ColonistBarDrawPatch replaces outright; the weapon beside it belongs to
            // ColonistBarOnGUI, and an agent generated from PawnKindDefOf.Colonist - a kind
            // carrying no weaponTags, in a colony that equips nothing - has none to draw.
            () => "VisibleMood".Translate(),
            () => "ShowWeaponsUnderPortrait".Translate(),

            // A gravship cutscene wants a gravship, which wants the quests and the Architect
            // menu this sim does not have.
            () => "GravshipCutscenes".Translate(),

            // Zoom-to-switch-layer is the world camera's and the remembered draw style is the
            // designator's: one view and one menu, neither of them reachable.
            () => "ZoomSwitchLayer".Translate(),
            () => "RememberDrawStyle".Translate(),
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
