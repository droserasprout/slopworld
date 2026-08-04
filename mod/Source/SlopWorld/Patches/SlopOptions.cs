using System.Collections.Generic;
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
        public static OptionCategoryDef UsageCategory { get; private set; }
        public static OptionCategoryDef AboutCategory { get; private set; }

        // The two plain-text headings in the column, not tabs: "SlopWorld" over our
        // pages and "RimWorld" over the game's. They take a slot for the column's fixed
        // pitch but are drawn as dim text and take no clicks, so never a selection.
        public static OptionCategoryDef SlopWorldLabel { get; private set; }
        public static OptionCategoryDef RimWorldLabel { get; private set; }

        // The terminal-appearance tab, between General and Usage. Same def pattern as the
        // others: Core's mod pack so it is drawn, and the gear icon is drawn by hand below.
        public static OptionCategoryDef TerminalCategory { get; private set; }

        // Rebuilt per open, so a config edited elsewhere - or a daemon that was down last
        // time - is re-read rather than remembered.
        static ConfigPage _page;
        static TerminalPage _terminalPage;
        static UsagePage _usagePage;
        static AboutPage _aboutPage;

        public static void Install()
        {
            if (Category != null) return;

            var general = OptionCategoryDefOf.General;
            if (general == null)
            {
                Log.Warning("[SlopWorld] no General option category to sit beside");
                return;
            }

            // The column, from the top down to where the game's own tabs begin: a
            // "SlopWorld" heading, the three pages under it - the config page named
            // General, the quotas, the About page - then a "RimWorld" heading above the
            // game's own categories. The headings are OptionCategoryDefs so they keep the
            // column's fixed pitch, but they are drawn as dim text and take no clicks
            // (Patch_OptionsRow_Section).
            SlopWorldLabel = Section("SlopWorld_Section", "SlopWorld", general);
            Category = Section("SlopWorld_Config", "General", general);
            TerminalCategory = Section("SlopWorld_Terminal", "Terminal", general);
            UsageCategory = Section("SlopWorld_Usage", "Usage", general);
            AboutCategory = Section("SlopWorld_About", "About", general);
            RimWorldLabel = Section("SlopWorld_RimWorldSection", "RimWorld", general);

            DefDatabase<OptionCategoryDef>.Add(SlopWorldLabel);
            DefDatabase<OptionCategoryDef>.Add(Category);
            DefDatabase<OptionCategoryDef>.Add(TerminalCategory);
            DefDatabase<OptionCategoryDef>.Add(UsageCategory);
            DefDatabase<OptionCategoryDef>.Add(AboutCategory);
            DefDatabase<OptionCategoryDef>.Add(RimWorldLabel);

            // AllDefsListForReading is the database's own list, and the column is drawn in
            // its order. Taken from wherever they sat and laid out at the head, with the
            // game's own categories following the "RimWorld" heading untouched.
            var all = DefDatabase<OptionCategoryDef>.AllDefsListForReading;
            all.Remove(SlopWorldLabel);
            all.Remove(Category);
            all.Remove(TerminalCategory);
            all.Remove(UsageCategory);
            all.Remove(AboutCategory);
            all.Remove(RimWorldLabel);
            all.Insert(0, SlopWorldLabel);
            all.Insert(1, Category);
            all.Insert(2, TerminalCategory);
            all.Insert(3, UsageCategory);
            all.Insert(4, AboutCategory);
            all.Insert(5, RimWorldLabel);
        }

        // One of the column's defs, all the same official-pack shape so fruit is drawn.
        // Dialog_Options draws a category only if its def came from an official mod, and
        // asks the def's own pack. Ours is Core's as far as that goes; the icon and the
        // row are drawn by hand below, so texPath is never read.
        static OptionCategoryDef Section(string defName, string label, OptionCategoryDef general)
        {
            return new OptionCategoryDef
            {
                defName = defName,
                label = label,
                modContentPack = general.modContentPack,
                texPath = general.texPath,
            };
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

        // From the General page, jump to the Terminal page in the dialog that is already
        // open. The "Appearance..." button used to open a floating window; with the pane's
        // settings a tab of this same dialog, the honest answer to the press is a tab swap.
        public static void OpenTerminalTab()
        {
            var w = Find.WindowStack?.WindowOfType<Dialog_Options>();
            if (w == null) return;
            w.selectedCategory = TerminalCategory;
            w.selectedMod = null;
        }

        // A save is a write of the *whole* file - every page here PUTs the sections it knows
        // about - so a page holding a copy read before that write would put the old figures
        // back the next time its own Save was pressed. Both re-read instead, including the
        // one that just saved, which costs a request and closes the hole.
        public static void Reread()
        {
            if (_page != null) _page.Load();
            if (_usagePage != null) _usagePage.Load();
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

        // The two headings in the column: "SlopWorld" above our pages and "RimWorld"
        // above the game's. Not a row - no background and no click, just the group's name
        // drawn dim in the slot a row would take, so the column's fixed pitch is kept.
        // Taking no click means one of these can never become the selected category.
        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow_Section
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                if (optionCategory != SlopWorldLabel && optionCategory != RimWorldLabel)
                    return true;

                Text.Font = GameFont.Small;
                GUI.color = SlopWidgets.Dim;
                Widgets.Label(new Rect(r.x + 10f, r.y, r.width - 20f, r.height),
                    optionCategory.label);
                GUI.color = Color.white;
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

        // Dropped on the way out, so the next open re-reads config.toml. The terminal
        // page also writes the settings file here, once, the way the window it replaced
        // did on close.
        [HarmonyPatch(typeof(Dialog_Options), nameof(Dialog_Options.PreClose))]
        public static class Patch_OptionsClose
        {
            static void Postfix()
            {
                _page = null; _terminalPage = null; _usagePage = null; _aboutPage = null;
                SlopWorldMod.Instance.settings.Write();
            }
        }


        // ---------------------------------------------------------------- terminal

        // The Terminal row, between General and Usage. Drawn with the gear icon that used
        // to live in the pane's title bar: the page is about the pane's look, and the gear
        // is the setting it was in the old floating window.
        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow_Terminal
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                if (optionCategory != TerminalCategory) return true;

                Widgets.DrawOptionBackground(r, __instance.selectedCategory == optionCategory);
                if (Widgets.ButtonInvisible(r))
                {
                    __instance.selectedCategory = optionCategory;
                    __instance.selectedMod = null;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                float x = r.x + 10f;
                GUI.DrawTexture(new Rect(x, r.y + (r.height - 20f) / 2f, 20f, 20f),
                    GearIcon.Tex);
                x += 30f;
                Widgets.Label(new Rect(x, r.y, r.width - x, r.height), optionCategory.label);
                return false;
            }
        }

        // The dispatch for the Terminal page, taken before vanilla's chain.
        [HarmonyPatch(typeof(Dialog_Options), "DoOptions")]
        public static class Patch_OptionsPage_Terminal
        {
            static bool Prefix(OptionCategoryDef category, Rect inRect)
            {
                if (category != TerminalCategory) return true;

                if (_terminalPage == null) _terminalPage = new TerminalPage();
                _terminalPage.Draw(inRect);
                return false;
            }
        }


        // ---------------------------------------------------------------- usage

        // The Usage row, between Terminal and About. Same shape as the row above, with a
        // lump of silver on it: the page is about resources, and the readout draws them
        // as the game's own.
        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow_Usage
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                if (optionCategory != UsageCategory) return true;

                Widgets.DrawOptionBackground(r, __instance.selectedCategory == optionCategory);
                if (Widgets.ButtonInvisible(r))
                {
                    __instance.selectedCategory = optionCategory;
                    __instance.selectedMod = null;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                float x = r.x + 10f;
                var icon = ThingDefOf.Silver;
                if (icon != null)
                {
                    Widgets.ThingIcon(new Rect(x, r.y + (r.height - 20f) / 2f, 20f, 20f), icon);
                    // ThingIcon leaves GUI.color on the def's own tint.
                    GUI.color = Color.white;
                }
                x += 30f;
                Widgets.Label(new Rect(x, r.y, r.width - x, r.height), optionCategory.label);
                return false;
            }
        }

        // The dispatch for the Usage page, taken before vanilla's chain for the reason the
        // config page's is.
        [HarmonyPatch(typeof(Dialog_Options), "DoOptions")]
        public static class Patch_OptionsPage_Usage
        {
            static bool Prefix(OptionCategoryDef category, Rect inRect)
            {
                if (category != UsageCategory) return true;

                if (_usagePage == null)
                {
                    _usagePage = new UsagePage();
                    _usagePage.Load();
                }
                _usagePage.Draw(inRect);
                return false;
            }
        }


        // ---------------------------------------------------------------- About

        // The About row, under the Usage row. Same shape as Patch_OptionsRow but with
        // the trophy emoji baked from tools/emoji.py rather than the blog icon that was
        // here when the page was about the game's build info.
        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow_About
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                if (optionCategory != AboutCategory) return true;

                Widgets.DrawOptionBackground(r, __instance.selectedCategory == optionCategory);
                if (Widgets.ButtonInvisible(r))
                {
                    __instance.selectedCategory = optionCategory;
                    __instance.selectedMod = null;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                float x = r.x + 10f;
                var icon = TrophyIcon.Tex;
                if (icon != null)
                    GUI.DrawTexture(new Rect(x, r.y + (r.height - 20f) / 2f, 20f, 20f), icon);
                x += 30f;
                Widgets.Label(new Rect(x, r.y, r.width - x, r.height), optionCategory.label);
                return false;
            }
        }

        // The dispatch for the About page, taken before the chain.
        [HarmonyPatch(typeof(Dialog_Options), "DoOptions")]
        public static class Patch_OptionsPage_About
        {
            static bool Prefix(OptionCategoryDef category, Rect inRect)
            {
                if (category != AboutCategory) return true;

                if (_aboutPage == null) _aboutPage = new AboutPage();
                _aboutPage.Draw(inRect);
                return false;
            }
        }


        // -------------------------------------------------------- main menu

        // The version info corner is drawn by VersionControl on every menu frame.
        // It moved to the About tab, so the corner is blank.
        [HarmonyPatch(typeof(VersionControl), nameof(VersionControl.DrawInfoInCorner))]
        public static class Patch_VersionCorner
        {
            static bool Prefix() => false;
        }

        // The web links column is what the main menu draws on the right. We suppress
        // it here; the same links are in the About tab. The list is identified by its
        // content: the game options are ListableOption, the web links are
        // ListableOption_WebLink. The About page also draws ListableOption_WebLink
        // inside Dialog_Options, which is gated by the window check.
        [HarmonyPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))]
        public static class Patch_WebLinks
        {
            static bool Prefix(List<ListableOption> optList)
            {
                // Only suppress lists that are purely web links.
                if (optList.Count == 0) return true;
                bool allLinks = true;
                foreach (var o in optList)
                    if (!(o is ListableOption_WebLink)) { allLinks = false; break; }
                if (!allLinks) return true;

                // Don't suppress if we're in the options dialog (About page draws
                // links there).
                if (Find.WindowStack?.currentlyDrawnWindow is Dialog_Options) return true;

                // Suppress on the main menu.
                optList.Clear();
                return true;
            }
        }

        // Expansion icons at the bottom of the main menu are left alone: the About
        // tab no longer draws its own copy of them.
    }
}