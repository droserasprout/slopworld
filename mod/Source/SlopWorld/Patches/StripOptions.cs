using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
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
            // KeyboardConfig is dropped: nothing exposes vanilla's Dialog_KeyBindings, and
            // the Library page is the one door for the mod's library items.
            () => "KeyboardConfig".Translate(),
            // Interface's UI scale row, a float menu over a fixed ladder with the rungs its
            // own guard rejects left out. Appearance has the slider, over the whole range
            // (see SlopUIScale); two doors to one pref, disagreeing about its span, is one
            // door too many.
            () => "UIScale".Translate(),

            // Resolution and borderless fullscreen are not useful in this window: the former
            // changes the render surface, while the latter is a launch-option notice rather
            // than a setting. The mod owns the live fullscreen toggle on Appearance.
            () => "Resolution".Translate(),
            () => "BorderlessFullscreen".Translate(),

            // These two vanilla Interface rows have native controls on the mod's General and
            // Appearance > Interface pages, so leave no duplicate behind in the old category.
            () => "DisableTinyText".Translate(),
            () => "TemperatureMode".Translate(),

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

        // Borderless fullscreen is emitted as a plain Label rather than a button: it is a
        // launch-option notice shown after the resolution button, so the button filter cannot
        // reach it. Return an empty rect so Listing_Standard does not leave a blank row.
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
