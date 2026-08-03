using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    // The options menu, made to look like the rest of this mod: the whole screen a pane
    // would have, with the page itself a centred band of readable width, and the daemon's
    // own configuration as the first category in the column.
    //
    // The room it takes is the room a terminal pane takes - everything the chrome is not,
    // so the column of agents and the line across the top stay where they are and this
    // opens inside them. Dialog_Options was the last thing that opened as a 650x600 panel
    // floating over the map, and with the config page moved into it there is more to draw
    // than that panel ever held. Centred with a cap on the width because a form stretched
    // across a 4K screen is a form nobody can read a row of.
    //
    // Vanilla lays this window out in *window* coordinates rather than off the rect it is
    // handed - the category column is a literal Rect(0, i*50, 160, 48) - so the band is a
    // GUI group rather than a remapped rect. Same reason InspectPaneUtility.DoTabs is
    // wrapped rather than shifted: the space a thing draws in is the only lever on a
    // layout computed from one figure. See ChromeShift.
    public static class SlopOptions
    {
        // As wide as the config page needs and no wider: 177 for the category column, the
        // rest for two columns of fields.
        const float MaxW = 1020f;
        // Room above the first category row. There is none below: the band runs to the
        // bottom of the window and the hidden OK button's row is what reads as padding.
        const float PadY = 24f;
        // What vanilla reserves at the foot of the page for the OK button. Handed back to
        // the options list, the button being gone.
        const float OkRow = 60f;

        // Ours, added at startup rather than shipped as XML: a def survives this mod
        // refusing to patch (see SlopProfile), and a category whose page is never drawn is
        // an empty tab in somebody else's options menu.
        public static OptionCategoryDef Category { get; private set; }

        // Rebuilt per open, so a config edited elsewhere - or a daemon that was down last
        // time - is re-read rather than remembered.
        static ConfigPage _page;

        public static void Install()
        {
            if (Category != null) return;

            var general = OptionCategoryDefOf.General;
            if (general == null)
            {
                Log.Warning("[SlopWorld] no General option category to sit beside");
                return;
            }

            Category = new OptionCategoryDef
            {
                defName = "SlopWorld_Config",
                label = "SlopWorld",
                // Dialog_Options draws a category only if its def came from an official
                // mod, and asks the def's own pack. Ours is Core's as far as that goes;
                // the icon and the row are drawn by hand below, so texPath is never read.
                modContentPack = general.modContentPack,
                texPath = general.texPath,
            };

            DefDatabase<OptionCategoryDef>.Add(Category);

            // AllDefsListForReading is the database's own list, and the column is drawn in
            // its order. Added last, moved to the front.
            var all = DefDatabase<OptionCategoryDef>.AllDefsListForReading;
            all.Remove(Category);
            all.Insert(0, Category);
        }

        // The `config` main button. Toggles rather than stacks, and opens on our own
        // category rather than on General.
        public static void Toggle()
        {
            var open = Find.WindowStack.WindowOfType<Dialog_Options>();
            if (open != null)
            {
                open.Close();
                return;
            }

            TerminalWindow.OpenOverPane(
                Category != null ? new Dialog_Options(Category) : new Dialog_Options());
        }

        // The screen less the chrome, which is the same room a pane gets. Zero inset on the
        // way in from the main menu: the column hangs off the colonist bar and the line off
        // a MapComponent, so with no colony behind it there is nothing to leave room for and
        // an inset would be a black margin around a window with nothing in it.
        static Rect Free()
        {
            bool playing = Current.ProgramState == ProgramState.Playing
                           && Find.CurrentMap != null;
            float left = playing ? SlopLayout.LeftInset : 0f;
            float top = playing ? SlopLayout.TopInset : 0f;
            return new Rect(left, top, UI.screenWidth - left, UI.screenHeight - top);
        }

        // Called when the layout is toggled on the page itself, so the window moves under
        // the checkbox rather than on the next open. Written on the event and not every
        // frame, the way ChromeShift moves the inspect pane.
        public static void Reposition()
        {
            var w = Find.WindowStack?.WindowOfType<Dialog_Options>();
            if (w != null) w.windowRect = Free();
        }

        // The band, in the coordinates of the rect the window hands its contents.
        static Rect Band(Rect r)
        {
            float w = Mathf.Min(r.width, MaxW);
            return new Rect(r.x + (r.width - w) / 2f, r.y + PadY, w, r.height - PadY);
        }


        [HarmonyPatch(typeof(Dialog_Options), nameof(Dialog_Options.InitialSize),
            MethodType.Getter)]
        public static class Patch_OptionsSize
        {
            static void Postfix(ref Vector2 __result)
            {
                var free = Free();
                __result = new Vector2(free.width, free.height);
            }
        }

        // The size above is centred by Window's own placement, and what is wanted is a
        // corner. Patched where the method is declared and gated on the instance, this
        // window overriding nothing - the same shape Patch_MainTabWindowShift takes.
        // It runs on open and on a resolution change, which is every time the figures move
        // on their own; the layout being toggled is the one that has to say so.
        [HarmonyPatch(typeof(Window), "SetInitialSizeAndPosition")]
        public static class Patch_OptionsPlace
        {
            static void Postfix(Window __instance)
            {
                if (__instance is Dialog_Options) __instance.windowRect = Free();
            }
        }

        // Opens the band as a group and hands the window a rect that starts at its corner.
        // The height handed over is the band's plus the row vanilla takes off for the OK
        // button, so the options list fills the band and the button - suppressed below -
        // is laid out past the bottom of the group.
        [HarmonyPatch(typeof(Dialog_Options), nameof(Dialog_Options.DoWindowContents))]
        public static class Patch_OptionsBand
        {
            // A BeginGroup without its End throws for the rest of the frame, so the
            // finalizer closes only what the prefix actually opened.
            static bool _grouped;

            static void Prefix(ref Rect inRect)
            {
                var band = Band(inRect);
                GUI.BeginGroup(band);
                _grouped = true;
                inRect = new Rect(0f, 0f, band.width, band.height + OkRow);
            }

            // A finalizer rather than a postfix: a postfix does not run when the original
            // throws, and a group left open is every window after it drawn in the wrong
            // place.
            static void Finalizer()
            {
                if (!_grouped) return;
                _grouped = false;
                GUI.EndGroup();
            }
        }

        // The window is the screen now, so there is nothing for an OK button to dismiss
        // that the corner cross and Escape do not. Void by returning what a button that
        // was not clicked returns; matched on the finished label the way StripOptions
        // matches its rows, and gated on the window rather than on a flag an exception
        // could strand.
        [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText),
            new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool),
                    typeof(TextAnchor?) })]
        public static class Patch_OptionsOk
        {
            static bool Prefix(string label, ref bool __result)
            {
                // The window first: this prefix is in front of every button in the game,
                // and the translation lookup is the expensive half of the question.
                if (!(Find.WindowStack?.currentlyDrawnWindow is Dialog_Options)) return true;

                string ok = "OK".Translate();
                if (label != ok) return true;

                __result = false;
                return false;
            }
        }

        // Vanilla's row with our own icon on it. The whole row rather than the texture,
        // because vanilla reads that off `texPath` through `ContentFinder`, which knows
        // about files, and the ">_" that means terminal everywhere here is drawn in code
        // (see TerminalIcon).
        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                if (optionCategory != Category) return true;

                Widgets.DrawOptionBackground(r, __instance.selectedCategory == optionCategory);
                if (Widgets.ButtonInvisible(r))
                {
                    __instance.selectedCategory = optionCategory;
                    __instance.selectedMod = null;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                float x = r.x + 10f;
                GUI.DrawTexture(new Rect(x, r.y + (r.height - 20f) / 2f, 20f, 20f),
                    TerminalIcon.Tex);
                x += 30f;
                Widgets.Label(new Rect(x, r.y, r.width - x, r.height), optionCategory.label);
                return false;
            }
        }

        // Vanilla's dispatch is a chain of comparisons against its own eight categories, so
        // ours would fall through it and draw nothing. Taken before the chain rather than
        // after: the page is two columns and its own scroll view, not rows on the
        // Listing_Standard vanilla opens here.
        [HarmonyPatch(typeof(Dialog_Options), "DoOptions")]
        public static class Patch_OptionsPage
        {
            static bool Prefix(OptionCategoryDef category, Rect inRect)
            {
                if (category != Category) return true;

                if (_page == null)
                {
                    _page = new ConfigPage();
                    _page.Load();
                }
                _page.Draw(inRect);
                return false;
            }
        }

        // Dropped on the way out, so the next open re-reads config.toml.
        [HarmonyPatch(typeof(Dialog_Options), nameof(Dialog_Options.PreClose))]
        public static class Patch_OptionsClose
        {
            static void Postfix() => _page = null;
        }
    }
}
