using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    // Options occupy the chrome's content area, with a width-capped page beside the
    // category column. Vanilla positions categories from window coordinates
    // (`Rect(0, i*50, 160, 48)`), so the centred band must be a GUI group; remapping its
    // rect would not move them. See OptionsView and ChromeShift.
    public static class SlopOptions
    {
        // The band, its width and the row taken off the foot for the OK button are
        // OptionsView's: they are about the shape the pages are drawn in, and this file is
        // the column of categories and the pages themselves.

        // Ours, added at startup rather than shipped as XML: a def survives this mod
        // refusing to patch (see SlopProfile), and a category whose page is never drawn is
        // an empty tab in somebody else's options menu.
        public static OptionCategoryDef Category { get; private set; }
        public static OptionCategoryDef StorageCategory { get; private set; }
        public static OptionCategoryDef UsageCategory { get; private set; }
        public static OptionCategoryDef SandboxCategory { get; private set; }
        public static OptionCategoryDef AboutCategory { get; private set; }

        // The two plain-text headings in the column, not tabs: "SlopWorld" over our
        // pages and "RimWorld" over the game's. They take a slot for the column's fixed
        // pitch but are drawn as dim text and take no clicks, so never a selection.
        public static OptionCategoryDef SlopWorldLabel { get; private set; }
        public static OptionCategoryDef RimWorldLabel { get; private set; }

        // The terminal-appearance tab, between General and Usage. Same def pattern as the
        // others: Core's mod pack so it is drawn, and the gear icon is drawn by hand below.
        public static OptionCategoryDef TerminalCategory { get; private set; }
        public static OptionCategoryDef AppearanceCategory { get; private set; }
        public static OptionCategoryDef AudioCategory { get; private set; }
        public static OptionCategoryDef KeyboardCategory { get; private set; }

        // Rebuilt per open, so a config edited elsewhere - or a daemon that was down last
        // time - is re-read rather than remembered.
        static ConfigPage _page;
        static StoragePage _storagePage;
        static TerminalPage _terminalPage;
        static AppearancePage _appearancePage;
        static AudioPage _audioPage;
        static UsagePage _usagePage;
        static SandboxPage _sandboxPage;
        static AboutPage _aboutPage;
        static KeyBindingsPage _keyBindingsPage;

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
            // "SlopWorld" heading, the pages under it - the config page named General,
            // storage, the terminal, the quotas, the sandbox, the About page - then a
            // "RimWorld" heading above the game's own categories. The headings are
            // OptionCategoryDefs so they keep the column's fixed pitch, but they are drawn
            // as dim text and take no clicks (Patch_OptionsRow_Section).
            SlopWorldLabel = Section("SlopWorld_Section", "SlopWorld", general);
            Category = Section("SlopWorld_Config", "General", general);
            StorageCategory = Section("SlopWorld_Storage", "Storage", general);
            TerminalCategory = Section("SlopWorld_Terminal", "Terminal", general);
            AppearanceCategory = Section("SlopWorld_Appearance", "Appearance", general);
            AudioCategory = Section("SlopWorld_Audio", "Audio", general);
            UsageCategory = Section("SlopWorld_Usage", "Usage", general);
            SandboxCategory = Section("SlopWorld_Sandbox", "Sandbox", general);
            KeyboardCategory = Section("SlopWorld_Keyboard", "Keyboard", general);
            AboutCategory = Section("SlopWorld_About", "About", general);
            RimWorldLabel = Section("SlopWorld_RimWorldSection", "RimWorld", general);

            DefDatabase<OptionCategoryDef>.Add(SlopWorldLabel);
            DefDatabase<OptionCategoryDef>.Add(Category);
            DefDatabase<OptionCategoryDef>.Add(StorageCategory);
            DefDatabase<OptionCategoryDef>.Add(TerminalCategory);
            DefDatabase<OptionCategoryDef>.Add(AppearanceCategory);
            DefDatabase<OptionCategoryDef>.Add(AudioCategory);
            DefDatabase<OptionCategoryDef>.Add(UsageCategory);
            DefDatabase<OptionCategoryDef>.Add(SandboxCategory);
            DefDatabase<OptionCategoryDef>.Add(KeyboardCategory);
            DefDatabase<OptionCategoryDef>.Add(AboutCategory);
            DefDatabase<OptionCategoryDef>.Add(RimWorldLabel);

            // AllDefsListForReading is the database's own list, and the column is drawn in
            // its order. Taken from wherever they sat and laid out at the head, with the
            // game's own categories following the "RimWorld" heading untouched.
            var all = DefDatabase<OptionCategoryDef>.AllDefsListForReading;
            all.Remove(SlopWorldLabel);
            all.Remove(Category);
            all.Remove(StorageCategory);
            all.Remove(TerminalCategory);
            all.Remove(AppearanceCategory);
            all.Remove(AudioCategory);
            all.Remove(UsageCategory);
            all.Remove(SandboxCategory);
            all.Remove(AboutCategory);
            all.Remove(KeyboardCategory);
            all.Remove(RimWorldLabel);
            all.Insert(0, SlopWorldLabel);
            all.Insert(1, Category);
            all.Insert(2, StorageCategory);
            all.Insert(3, TerminalCategory);
            all.Insert(4, AppearanceCategory);
            all.Insert(5, AudioCategory);
            all.Insert(6, UsageCategory);
            all.Insert(7, SandboxCategory);
            all.Insert(8, KeyboardCategory);
            all.Insert(9, AboutCategory);
            all.Insert(10, RimWorldLabel);

            // Its five controls live on our Audio page now. Keep the def in the database,
            // as with Gameplay, but omit its duplicate row from the ordinary options list.
            if (OptionCategoryDefOf.Audio != null) OptionCategoryDefOf.Audio.isDev = true;
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

        // Which page the view was on when it was last left. A toggle builds a *new* view
        // every time it opens one - the dialog behind it is rebuilt so the pages re-read
        // config.toml - so without this the gear and the palette's "Settings" both dropped
        // the reader back on General each time, and a switch that forgets where it was is a
        // switch you cannot use to glance at something. In memory only: which tab you were
        // reading is about this sitting and not something to write to a settings file.
        static OptionCategoryDef _lastCategory;

        // From OptionsView.Closed, which is the one road out of the view.
        public static void Remember(OptionCategoryDef category)
        {
            if (category != null) _lastCategory = category;
        }

        // The `config` main button. Toggles rather than stacks, and opens on the page it was
        // last left on - our own category the first time, rather than on General. Content
        // rather than a window (see OptionsView): the menu is laid out inside the chrome, so
        // being a window over it was what took every press off the column underneath.
        public static void Toggle() =>
            TerminalWindow.ToggleContent(() => new OptionsView(_lastCategory ?? Category));

        // A palette entry can name a page directly. Open the options view when it is not
        // already up, or swap the category in the existing view - the same two roads as the
        // gear and the page's own links, without making the palette know about content views.
        public static void OpenCategory(OptionCategoryDef category)
        {
            if (category == null) return;

            var v = TerminalWindow.ShowingAs<OptionsView>();
            if (v == null) TerminalWindow.OpenContent(new OptionsView(category));
            else v.Category = category;
        }

        // From the General page, jump to the Terminal page in the view that is already up.
        // The "Appearance..." button used to open a floating window; with the pane's
        // settings a tab of this same page, the honest answer to the press is a tab swap.
        public static void OpenTerminalTab()
        {
            var v = TerminalWindow.ShowingAs<OptionsView>();
            if (v != null) v.Category = TerminalCategory;
        }

        // The jukebox menu's Settings row opens our mixer directly.
        public static void OpenAudioTab()
        {
            var v = TerminalWindow.ShowingAs<OptionsView>();
            if (v == null)
            {
                TerminalWindow.OpenContent(new OptionsView(AudioCategory));
                return;
            }
            if (AudioCategory != null) v.Category = AudioCategory;
        }

        // A save is a write of the *whole* file - every page here PUTs the sections it knows
        // about - so a page holding a copy read before that write would put the old figures
        // back the next time its own Save was pressed. Both re-read instead, including the
        // one that just saved, which costs a request and closes the hole.
        public static void Reread()
        {
            if (_page != null) _page.Load();
            if (_storagePage != null) _storagePage.Load();
            if (_usagePage != null) _usagePage.Load();
            if (_sandboxPage != null) _sandboxPage.Load();
        }

        // Dropped when the view is left, so the next open re-reads config.toml. The terminal
        // page also writes the settings file here, once, the way the window it replaced did
        // on close. Called from OptionsView.Closed, and from the PreClose patch below for
        // the dialog the *main menu* still opens as a window of its own.
        public static void Teardown()
        {
            _page = null; _storagePage = null; _terminalPage = null; _appearancePage = null;
            _audioPage = null;
            _usagePage = null;
            _sandboxPage = null; _aboutPage = null; _keyBindingsPage = null;
            SlopWorldMod.Instance.settings.Write();
        }

        // The band is the content rect now, so there is nothing for an OK button to dismiss
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
                // Who is drawing first: this prefix is in front of every button in the game,
                // and the translation lookup is the expensive half of the question.
                if (!OptionsView.Anywhere) return true;

                string ok = "OK".Translate();
                if (label != ok) return true;

                __result = false;
                return false;
            }
        }

        // Vanilla's option background is a texture with a border and a hover of its own.
        // This is the same row in the list's own colours: the accent tint for the page being
        // read, plain white for the one under the mouse, nothing for the rest. Every row this
        // mod adds to the column draws through it, which is why it is one method.
        static void CategoryRow(Rect r, bool selected)
        {
            if (selected) Slab.Fill(r, SlopWidgets.Sel);
            else if (Mouse.IsOver(r)) Slab.Fill(r, SlopWidgets.Hover);
        }

        // Vanilla's row with our own icon on it. The whole row rather than the texture,
        // because vanilla reads that off `texPath` through `ContentFinder`, which knows
        // about files, and the general-config page gets the gear: it is the tab that opens
        // the settings you can get to anywhere, and the gear is the icon for settings.
        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                if (optionCategory != Category) return true;

                CategoryRow(r, __instance.selectedCategory == optionCategory);
                if (Widgets.ButtonInvisible(r))
                {
                    __instance.selectedCategory = optionCategory;
                    __instance.selectedMod = null;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                float x = r.x + 10f;
                GUI.DrawTexture(new Rect(x, r.y + (r.height - 20f) / 2f, 20f, 20f),
                    Icons.Gear);
                x += 30f;
                Widgets.Label(new Rect(x, r.y, r.xMax - x, r.height), optionCategory.LabelCap);
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

        // ---------------------------------------------------------------- storage

        // Private state is a page rather than a button buried in General: it is an inventory
        // with its own destination, and selecting an entry hands that directory to Files.
        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow_Storage
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                if (optionCategory != StorageCategory) return true;

                CategoryRow(r, __instance.selectedCategory == optionCategory);
                if (Widgets.ButtonInvisible(r))
                {
                    __instance.selectedCategory = optionCategory;
                    __instance.selectedMod = null;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                float x = r.x + 10f;
                GUI.DrawTexture(new Rect(x, r.y + (r.height - 20f) / 2f, 20f, 20f), Icons.Files);
                x += 30f;
                Widgets.Label(new Rect(x, r.y, r.xMax - x, r.height), optionCategory.LabelCap);
                return false;
            }
        }

        [HarmonyPatch(typeof(Dialog_Options), "DoOptions")]
        public static class Patch_OptionsPage_Storage
        {
            static bool Prefix(OptionCategoryDef category, Rect inRect)
            {
                if (category != StorageCategory) return true;

                if (_storagePage == null)
                {
                    _storagePage = new StoragePage();
                    _storagePage.Load();
                }
                _storagePage.Draw(inRect);
                return false;
            }
        }

        // Dialog_Options() opens vanilla General by default. That category is stripped, so
        // the main menu road must not begin on a selected page with no row in the column.
        // Explicit constructors for our content view already name their destination.
        [HarmonyPatch(typeof(Dialog_Options), nameof(Dialog_Options.PostOpen))]
        public static class Patch_OptionsDefaultCategory
        {
            static void Postfix(Dialog_Options __instance)
            {
                if (__instance.selectedCategory == OptionCategoryDefOf.General)
                {
                    __instance.selectedCategory = Category;
                    __instance.selectedMod = null;
                }
            }
        }

        // ---------------------------------------------------------------- appearance

        // The Appearance row, between Terminal and Usage. A typeface icon: the page is
        // about the mod's font, and the "Aa" is what says "text" without a word.
        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow_Appearance
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                if (optionCategory != AppearanceCategory) return true;

                CategoryRow(r, __instance.selectedCategory == optionCategory);
                if (Widgets.ButtonInvisible(r))
                {
                    __instance.selectedCategory = optionCategory;
                    __instance.selectedMod = null;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                float x = r.x + 10f;
                GUI.DrawTexture(new Rect(x, r.y + (r.height - 20f) / 2f, 20f, 20f),
                    Icons.Type);
                x += 30f;
                Widgets.Label(new Rect(x, r.y, r.xMax - x, r.height), optionCategory.LabelCap);
                return false;
            }
        }

        // The dispatch for the Appearance page, taken before vanilla's chain.
        [HarmonyPatch(typeof(Dialog_Options), "DoOptions")]
        public static class Patch_OptionsPage_Appearance
        {
            static bool Prefix(OptionCategoryDef category, Rect inRect)
            {
                if (category != AppearanceCategory) return true;

                if (_appearancePage == null) _appearancePage = new AppearancePage();
                _appearancePage.Draw(inRect);
                return false;
            }
        }

        // ---------------------------------------------------------------- audio

        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow_Audio
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                if (optionCategory != AudioCategory) return true;

                CategoryRow(r, __instance.selectedCategory == optionCategory);
                if (Widgets.ButtonInvisible(r))
                {
                    __instance.selectedCategory = optionCategory;
                    __instance.selectedMod = null;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                float x = r.x + 10f;
                GUI.DrawTexture(new Rect(x, r.y + (r.height - 20f) / 2f, 20f, 20f),
                    Icons.Bell);
                x += 30f;
                Widgets.Label(new Rect(x, r.y, r.xMax - x, r.height), optionCategory.LabelCap);
                return false;
            }
        }

        [HarmonyPatch(typeof(Dialog_Options), "DoOptions")]
        public static class Patch_OptionsPage_Audio
        {
            static bool Prefix(OptionCategoryDef category, Rect inRect)
            {
                if (category != AudioCategory) return true;

                if (_audioPage == null) _audioPage = new AudioPage();
                _audioPage.Draw(inRect);
                return false;
            }
        }


        // ---------------------------------------------------------------- the main menu
        //
        // Out there the dialog is still a window: there is no chrome to be content inside of,
        // the column hanging off the colonist bar and the line off a MapComponent. So the
        // three patches that shaped that window are kept, and only that road reaches them -
        // the view calls DoWindowContents itself, and opens the band on the body rect.

        // The screen less the chrome, which is the same room a pane gets. Zero inset from the
        // main menu: with no colony behind it there is nothing to leave room for, and an inset
        // would be a black margin around a window with nothing in it.
        static Rect Free()
        {
            bool playing = Current.ProgramState == ProgramState.Playing
                           && Find.CurrentMap != null;
            float left = playing ? SlopLayout.LeftInset : 0f;
            float top = playing ? SlopLayout.TopInset : 0f;
            return new Rect(left, top, UI.screenWidth - left, UI.screenHeight - top);
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

        // The size above is centred by Window's own placement, and what is wanted is a corner.
        // Patched where the method is declared and gated on the instance, this window
        // overriding nothing - the same shape Patch_MainTabWindowShift takes.
        [HarmonyPatch(typeof(Window), "SetInitialSizeAndPosition")]
        public static class Patch_OptionsPlace
        {
            static void Postfix(Window __instance)
            {
                if (__instance is Dialog_Options) __instance.windowRect = Free();
            }
        }

        // The band, for the window road only: OptionsView opens its own on the body rect, and
        // a second one here would centre the page inside the page.
        [HarmonyPatch(typeof(Dialog_Options), nameof(Dialog_Options.DoWindowContents))]
        public static class Patch_OptionsBand
        {
            // A BeginGroup without its End throws for the rest of the frame, so the finalizer
            // closes only what the prefix actually opened.
            static bool _grouped;

            static void Prefix(ref Rect inRect)
            {
                if (OptionsView.Drawing) return;
                var band = OptionsView.Band(inRect);
                GUI.BeginGroup(band);
                _grouped = true;
                inRect = OptionsView.Inner(band);
            }

            static void Finalizer()
            {
                if (!_grouped) return;
                _grouped = false;
                GUI.EndGroup();
            }
        }

        // The pages the window built are dropped the way they always were; the view has its
        // own road to the same teardown.
        [HarmonyPatch(typeof(Dialog_Options), nameof(Dialog_Options.PreClose))]
        public static class Patch_OptionsClose
        {
            static void Postfix() => Teardown();
        }

        // F1 and F12 are the chrome's own now (TerminalWindow.ChromeKeys), the options menu
        // being drawn *by* that window rather than by one over it. What used to be answered
        // here - a patch on the dialog, because a window absorbing input made
        // HandleEventsHighPriority use every KeyDown before anything below it could hear one
        // - is one of the things the content view took away.


        // ---------------------------------------------------------------- terminal

        // The Terminal row, between General and Usage. Drawn with the terminal icon that
        // used to be on the General tab: the page is about the pane's look, and the ">_"
        // is what said "terminal" before the settings moved into a tab of their own.
        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow_Terminal
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                if (optionCategory != TerminalCategory) return true;

                CategoryRow(r, __instance.selectedCategory == optionCategory);
                if (Widgets.ButtonInvisible(r))
                {
                    __instance.selectedCategory = optionCategory;
                    __instance.selectedMod = null;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                float x = r.x + 10f;
                GUI.DrawTexture(new Rect(x, r.y + (r.height - 20f) / 2f, 20f, 20f),
                    Icons.Terminal);
                x += 30f;
                Widgets.Label(new Rect(x, r.y, r.xMax - x, r.height), optionCategory.LabelCap);
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

        // The Usage row, between Terminal and About. Same shape as the row above, wearing
        // the card of Icons.Usage. It was a lump of ThingDefOf.Silver, on the grounds that
        // the readout draws spend as the game's own resource - but that is the readout's
        // joke, and in the column it was the one row not drawn from the icon set, tinted
        // by the def rather than by the row.
        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow_Usage
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                if (optionCategory != UsageCategory) return true;

                CategoryRow(r, __instance.selectedCategory == optionCategory);
                if (Widgets.ButtonInvisible(r))
                {
                    __instance.selectedCategory = optionCategory;
                    __instance.selectedMod = null;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                float x = r.x + 10f;
                var icon = Icons.Usage;
                if (icon != null)
                    GUI.DrawTexture(new Rect(x, r.y + (r.height - 20f) / 2f, 20f, 20f), icon);
                x += 30f;
                Widgets.Label(new Rect(x, r.y, r.xMax - x, r.height), optionCategory.LabelCap);
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


        // ---------------------------------------------------------------- sandbox

        // The Sandbox row, between Usage and About. A wall: the page is about the ground
        // every agent runs on, and the shield is the icon for a barrier.
        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow_Sandbox
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                if (optionCategory != SandboxCategory) return true;

                CategoryRow(r, __instance.selectedCategory == optionCategory);
                if (Widgets.ButtonInvisible(r))
                {
                    __instance.selectedCategory = optionCategory;
                    __instance.selectedMod = null;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                float x = r.x + 10f;
                GUI.DrawTexture(new Rect(x, r.y + (r.height - 20f) / 2f, 20f, 20f),
                    Icons.Shield);
                x += 30f;
                Widgets.Label(new Rect(x, r.y, r.xMax - x, r.height), optionCategory.LabelCap);
                return false;
            }
        }

        // The dispatch for the Sandbox page, taken before vanilla's chain. Loaded on first
        // draw the way the other pages are, so the daemon re-reads config.toml each open.
        [HarmonyPatch(typeof(Dialog_Options), "DoOptions")]
        public static class Patch_OptionsPage_Sandbox
        {
            static bool Prefix(OptionCategoryDef category, Rect inRect)
            {
                if (category != SandboxCategory) return true;

                if (_sandboxPage == null)
                {
                    _sandboxPage = new SandboxPage();
                    _sandboxPage.Load();
                }
                _sandboxPage.Draw(inRect);
                return false;
            }
        }


        // ---------------------------------------------------------------- About

        // The About row, under the Sandbox row. Same shape as Patch_OptionsRow but with
        // Icons.Trophy rather than the blog icon that was here when the page was about
        // the game's build info.
        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow_About
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                if (optionCategory != AboutCategory) return true;

                CategoryRow(r, __instance.selectedCategory == optionCategory);
                if (Widgets.ButtonInvisible(r))
                {
                    __instance.selectedCategory = optionCategory;
                    __instance.selectedMod = null;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                float x = r.x + 10f;
                var icon = Icons.Trophy;
                if (icon != null)
                    GUI.DrawTexture(new Rect(x, r.y + (r.height - 20f) / 2f, 20f, 20f), icon);
                x += 30f;
                Widgets.Label(new Rect(x, r.y, r.xMax - x, r.height), optionCategory.LabelCap);
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


        // ----------------------------------------------------------------- keyboard

        // The Keyboard row, over the About row. Same shape as the rows above, wearing the
        // keyboard of Icons.Keyboard: the page is the key bindings, and both the label and
        // the glyph say so outright. It was the lightning bolt of Icons.Shortcuts under
        // the label "Shortcuts", which is the sidebar's errands - the same two words and
        // the same picture for two unrelated things.
        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow_Keyboard
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                if (optionCategory != KeyboardCategory) return true;

                CategoryRow(r, __instance.selectedCategory == optionCategory);
                if (Widgets.ButtonInvisible(r))
                {
                    __instance.selectedCategory = optionCategory;
                    __instance.selectedMod = null;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                float x = r.x + 10f;
                var icon = Icons.Keyboard;
                if (icon != null)
                    GUI.DrawTexture(new Rect(x, r.y + (r.height - 20f) / 2f, 20f, 20f), icon);
                x += 30f;
                Widgets.Label(new Rect(x, r.y, r.xMax - x, r.height), optionCategory.LabelCap);
                return false;
            }
        }

        // The dispatch for the Keyboard page, taken before vanilla's chain.
        [HarmonyPatch(typeof(Dialog_Options), "DoOptions")]
        public static class Patch_OptionsPage_Keyboard
        {
            static bool Prefix(OptionCategoryDef category, Rect inRect)
            {
                if (category != KeyboardCategory) return true;

                if (_keyBindingsPage == null) _keyBindingsPage = new KeyBindingsPage();
                _keyBindingsPage.Draw(inRect);
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
                if (OptionsView.Anywhere) return true;

                // Suppress on the main menu.
                optList.Clear();
                return true;
            }
        }

        // Expansion icons at the bottom of the main menu are left alone: the About
        // tab no longer draws its own copy of them.
    }
}
