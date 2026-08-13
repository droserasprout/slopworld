using System;
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

        // A child row is indented and iconless; a page-less parent selects its first child.
        class Tab
        {
            public OptionCategoryDef Def;
            public Func<Texture2D> Icon;
            public Action<Rect> Page;
            public Tab Parent;
            public bool Synthetic;
        }

        // Column order matches the visible category order, and synthetic entries are added
        // at startup.
        static readonly List<Tab> Column = new List<Tab>();

        static Tab _config, _storage, _terminal, _appearance, _audio, _integrations,
                   _usage, _summaries, _sandbox, _keyboard, _rimworld, _about;

        public static OptionCategoryDef Category => _config?.Def;
        public static OptionCategoryDef StorageCategory => _storage?.Def;
        public static OptionCategoryDef TerminalCategory => _terminal?.Def;
        public static OptionCategoryDef AppearanceCategory => _appearance?.Def;
        public static OptionCategoryDef AudioCategory => _audio?.Def;
        public static OptionCategoryDef IntegrationsCategory => _integrations?.Def;
        public static OptionCategoryDef UsageCategory => _usage?.Def;
        public static OptionCategoryDef SummariesCategory => _summaries?.Def;
        public static OptionCategoryDef SandboxCategory => _sandbox?.Def;
        public static OptionCategoryDef KeyboardCategory => _keyboard?.Def;
        public static OptionCategoryDef RimWorldCategory => _rimworld?.Def;
        public static OptionCategoryDef AboutCategory => _about?.Def;

        // Rebuilt per open, so a config edited elsewhere - or a daemon that was down last
        // time - is re-read rather than remembered.
        static ConfigPage _page;
        static StoragePage _storagePage;
        static TerminalPage _terminalPage;
        static AppearancePage _appearancePage;
        static AudioPage _audioPage;
        static UsagePage _usagePage;
        static SummariesPage _summariesPage;
        static SandboxPage _sandboxPage;
        static AboutPage _aboutPage;
        static KeyBindingsPage _keyBindingsPage;

        public static void Install()
        {
            if (Column.Count > 0) return;

            var general = OptionCategoryDefOf.General;
            if (general == null)
            {
                Log.Warning("[SlopWorld] no General option category to sit beside");
                return;
            }

            _config = Add("SlopWorld_Config", "General", general, () => Icons.Gear, DrawConfig);
            _storage = Add("SlopWorld_Storage", "Storage", general, () => Icons.Files,
                DrawStorage);
            _terminal = Add("SlopWorld_Terminal", "Terminal", general, () => Icons.Terminal,
                DrawTerminal);
            _appearance = Add("SlopWorld_Appearance", "Appearance", general, () => Icons.Type,
                DrawAppearance);
            _audio = Add("SlopWorld_Audio", "Audio", general, () => Icons.Bell, DrawAudio);
            _integrations = Add("SlopWorld_Integrations", "Integrations", general,
                () => Icons.Usage, null);
            _usage = Add("SlopWorld_Usage", "Usage", general, null, DrawUsage, _integrations);
            _summaries = Add("SlopWorld_Summaries", "Summaries", general, null, DrawSummaries,
                _integrations);
            _sandbox = Add("SlopWorld_Sandbox", "Sandbox", general, () => Icons.Shield,
                DrawSandbox);
            _keyboard = Add("SlopWorld_Keyboard", "Keyboard", general, () => Icons.Keyboard,
                DrawKeyboard);
            _rimworld = Add("SlopWorld_RimWorld", "RimWorld", general, () => Icons.RimWorld,
                null);
            Existing(OptionCategoryDefOf.Graphics, _rimworld);
            Existing(OptionCategoryDefOf.Interface, _rimworld);
            Existing(OptionCategoryDefOf.Controls, _rimworld);
            _about = Add("SlopWorld_About", "About", general, () => Icons.Trophy, DrawAbout);

            foreach (var tab in Column)
                if (tab.Synthetic) DefDatabase<OptionCategoryDef>.Add(tab.Def);

            // Move column entries to the front of the database in column order; remaining
            // game categories follow About.
            var all = DefDatabase<OptionCategoryDef>.AllDefsListForReading;
            for (int i = 0; i < Column.Count; i++)
            {
                all.Remove(Column[i].Def);
                all.Insert(i, Column[i].Def);
            }

            // Its five controls live on our Audio page now. Keep the def in the database,
            // as with Gameplay, but omit its duplicate row from the ordinary options list.
            if (OptionCategoryDefOf.Audio != null) OptionCategoryDefOf.Audio.isDev = true;
        }

        static Tab Add(string defName, string label, OptionCategoryDef general,
            Func<Texture2D> icon, Action<Rect> page, Tab parent = null)
        {
            var tab = new Tab
            {
                Def = MakeDef(defName, label, general),
                Icon = icon,
                Page = page,
                Parent = parent,
                Synthetic = true,
            };
            Column.Add(tab);
            return tab;
        }

        static Tab Existing(OptionCategoryDef def, Tab parent)
        {
            if (def == null) return null;

            var tab = new Tab { Def = def, Parent = parent };
            Column.Add(tab);
            return tab;
        }

        // Reuse General's content pack so Dialog_Options accepts the synthetic category; rows
        // and icons are drawn here, so `texPath` is unused.
        static OptionCategoryDef MakeDef(string defName, string label, OptionCategoryDef general)
        {
            return new OptionCategoryDef
            {
                defName = defName,
                label = label,
                modContentPack = general.modContentPack,
                texPath = general.texPath,
            };
        }

        static Tab TabOf(OptionCategoryDef category)
        {
            if (category == null) return null;
            for (int i = 0; i < Column.Count; i++)
                if (Column[i].Def == category) return Column[i];
            return null;
        }

        // Null is not a parent here: every top-level tab carries a null one, so an unmatched
        // category asking for its children would be handed the head of the column.
        static Tab FirstChild(Tab parent)
        {
            if (parent == null) return null;
            for (int i = 0; i < Column.Count; i++)
                if (Column[i].Parent == parent) return Column[i];
            return null;
        }

        // What a press on a row selects. A tab with no page of its own is a heading with
        // pages under it, so the press opens the first of them.
        static Tab Target(Tab tab) =>
            tab == null || tab.Page != null ? tab : FirstChild(tab);

        // ---------------------------------------------------------------- the pages

        static void DrawConfig(Rect r)
        {
            if (_page == null)
            {
                _page = new ConfigPage();
                _page.Load();
            }
            _page.Draw(r);
        }

        static void DrawStorage(Rect r)
        {
            if (_storagePage == null)
            {
                _storagePage = new StoragePage();
                _storagePage.Load();
            }
            _storagePage.Draw(r);
        }

        static void DrawTerminal(Rect r)
        {
            if (_terminalPage == null) _terminalPage = new TerminalPage();
            _terminalPage.Draw(r);
        }

        static void DrawAppearance(Rect r)
        {
            if (_appearancePage == null) _appearancePage = new AppearancePage();
            _appearancePage.Draw(r);
        }

        static void DrawAudio(Rect r)
        {
            if (_audioPage == null) _audioPage = new AudioPage();
            _audioPage.Draw(r);
        }

        static void DrawUsage(Rect r)
        {
            if (_usagePage == null)
            {
                _usagePage = new UsagePage();
                _usagePage.Load();
            }
            _usagePage.Draw(r);
        }

        static void DrawSummaries(Rect r)
        {
            if (_summariesPage == null)
            {
                _summariesPage = new SummariesPage();
                _summariesPage.Load();
            }
            _summariesPage.Draw(r);
        }

        static void DrawSandbox(Rect r)
        {
            if (_sandboxPage == null)
            {
                _sandboxPage = new SandboxPage();
                _sandboxPage.Load();
            }
            _sandboxPage.Draw(r);
        }

        static void DrawKeyboard(Rect r)
        {
            if (_keyBindingsPage == null) _keyBindingsPage = new KeyBindingsPage();
            _keyBindingsPage.Draw(r);
        }

        static void DrawAbout(Rect r)
        {
            if (_aboutPage == null) _aboutPage = new AboutPage();
            _aboutPage.Draw(r);
        }

        // Preserve the last page because each toggle rebuilds the view and reloads config.
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

            var target = Target(TabOf(category));
            if (target != null) category = target.Def;

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
            if (_summariesPage != null) _summariesPage.Load();
            if (_sandboxPage != null) _sandboxPage.Load();
        }

        // Drop page instances and persist settings when the view/window closes.
        public static void Teardown()
        {
            _page = null; _storagePage = null; _terminalPage = null; _appearancePage = null;
            _audioPage = null;
            _usagePage = null; _summariesPage = null;
            _sandboxPage = null; _aboutPage = null; _keyBindingsPage = null;
            SlopWorldMod.Instance.settings.Write();
        }

        // The content view has no OK button; suppress only vanilla's translated OK button
        // while the options view is active.
        [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText),
            new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool),
                    typeof(TextAnchor?) })]
        public static class Patch_OptionsOk
        {
            static bool Prefix(string label, ref bool __result)
            {
                // This prefix sees every button, so gate before translating the label.
                if (!OptionsView.Anywhere) return true;

                string ok = "OK".Translate();
                if (label != ok) return true;

                __result = false;
                return false;
            }
        }

        // ---------------------------------------------------------------- the column

        // Children omit the icon and indent their labels by ChildIndent.
        const float RowPadX = 10f;
        const float ChildIndent = 12f;
        const float IconGap = 10f;

        // What vanilla lays a row out on: `Rect(0, i * 50, 160, 48)` contracted by 4, which
        // is the only place the row's index is available to read back.
        const float VanillaPitch = 50f;
        const float VanillaInset = 4f;

        // Vanilla reserves a 50px pitch, so top-level rows use the larger computed pitch.
        static float RowH => Mathf.Round(SlopWidgets.LineH * 1.5f);
        static float NestedRowH => Mathf.Round(SlopWidgets.LineH * 1.25f);
        static float Pitch => RowH + SlopWidgets.GapXS;
        static float NestedPitch => NestedRowH + SlopWidgets.GapXS;

        // Limit the icon box to the row height.
        static float IconBox => Mathf.Min(20f, RowH - 6f);
        static float LabelX => RowPadX + IconBox + IconGap;

        // Recover the vanilla row index from r.y and recompute the compact row rectangle.
        static Rect Slot(Rect r, Tab tab)
        {
            int i = Mathf.Max(0, Mathf.RoundToInt((r.y - VanillaInset) / VanillaPitch));
            float y = VanillaInset;
            for (int n = 0; n < i; n++)
                y += n < Column.Count && Column[n].Parent != null ? NestedPitch : Pitch;

            float h = tab != null && tab.Parent != null ? NestedRowH : RowH;
            return new Rect(r.x, y, r.width, h);
        }

        // Draw selected and hovered rows with the mod's colors.
        static void CategoryRow(Rect r, bool selected)
        {
            if (selected) Slab.Fill(r, SlopWidgets.Sel);
            else if (Mouse.IsOver(r)) Slab.Fill(r, SlopWidgets.Hover);
        }

        static void Select(Dialog_Options dlg, OptionCategoryDef category)
        {
            dlg.selectedCategory = category;
            dlg.selectedMod = null;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        // Draw vanilla categories here so nested game rows use the compact layout.
        [HarmonyPatch(typeof(Dialog_Options), "DoCategoryRow")]
        public static class Patch_OptionsRow
        {
            static bool Prefix(Dialog_Options __instance, Rect r, OptionCategoryDef optionCategory)
            {
                var tab = TabOf(optionCategory);
                var row = Slot(r, tab);
                Text.Font = GameFont.Small;

                CategoryRow(row, __instance.selectedCategory == optionCategory);
                if (Widgets.ButtonInvisible(row))
                {
                    var target = Target(tab);
                    Select(__instance, target != null ? target.Def : optionCategory);
                }

                float x = row.x + LabelX;
                if (tab != null && tab.Parent != null)
                {
                    x += ChildIndent;
                }
                else
                {
                    var icon = tab != null
                        ? tab.Icon?.Invoke()
                        : ContentFinder<Texture2D>.Get(optionCategory.texPath);
                    if (icon != null)
                        GUI.DrawTexture(
                            new Rect(row.x + RowPadX, row.y + (row.height - IconBox) / 2f,
                                IconBox, IconBox), icon);
                }

                SlopWidgets.RowLabel(new Rect(x, row.y, row.xMax - x, row.height),
                    optionCategory.LabelCap);
                return false;
            }
        }

        // Vanilla's dispatch is a chain of comparisons against its own eight categories, so
        // ours would fall through it and draw nothing. Taken before the chain rather than
        // after: a page is two columns and its own scroll view, not rows on the
        // Listing_Standard vanilla opens here.
        [HarmonyPatch(typeof(Dialog_Options), "DoOptions")]
        public static class Patch_OptionsPage
        {
            static bool Prefix(OptionCategoryDef category, Rect inRect)
            {
                var page = Target(TabOf(category))?.Page;
                if (page == null) return true;

                page(inRect);
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


        // ---------------------------------------------------------------- the main menu
        // Main-menu options remain a real window; the content view applies the same layout itself.

        // Reserve chrome in-game; use the full screen on the main menu.
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

        // Override vanilla's centred placement with the chrome-aligned rect.
        [HarmonyPatch(typeof(Window), "SetInitialSizeAndPosition")]
        public static class Patch_OptionsPlace
        {
            static void Postfix(Window __instance)
            {
                if (__instance is Dialog_Options) __instance.windowRect = Free();
            }
        }

        // Apply the band only to vanilla's window path; OptionsView opens its own group.
        [HarmonyPatch(typeof(Dialog_Options), nameof(Dialog_Options.DoWindowContents))]
        public static class Patch_OptionsBand
        {
            // Close only a group opened by this prefix.
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


        // -------------------------------------------------------- main menu

        // The version info corner is drawn by VersionControl on every menu frame.
        // It moved to the About tab, so the corner is blank.
        [HarmonyPatch(typeof(VersionControl), nameof(VersionControl.DrawInfoInCorner))]
        public static class Patch_VersionCorner
        {
            static bool Prefix() => false;
        }

        // Hide the main-menu web-link list; the About tab owns those links.
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
