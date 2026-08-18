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

        public enum PageId
        {
            Config,
            Commands,
            CommandDefaults,
            CommandPresets,
            Storage,
            Appearance,
            AppearanceInterface,
            Terminal,
            Audio,
            Integrations,
            Credentials,
            Usage,
            Summaries,
            Sandbox,
            Keyboard,
            RimWorld,
            About,
        }

        sealed class TabSpec
        {
            public readonly PageId Key;
            public readonly string DefName;
            public readonly string Label;
            public readonly Func<OptionCategoryDef> Existing;
            public readonly Func<Texture2D> Icon;
            public readonly Func<IOptionPage> PageFactory;
            public readonly PageId? Parent;

            public TabSpec(PageId key, string defName, string label,
                Func<Texture2D> icon, Func<IOptionPage> pageFactory,
                PageId? parent = null)
            {
                Key = key;
                DefName = defName;
                Label = label;
                Existing = null;
                Icon = icon;
                PageFactory = pageFactory;
                Parent = parent;
            }

            public TabSpec(PageId key, Func<OptionCategoryDef> existing, PageId? parent = null)
            {
                Key = key;
                DefName = null;
                Label = null;
                Existing = existing;
                Icon = null;
                PageFactory = null;
                Parent = parent;
            }
        }

        // Column order matches the visible category order. Synthetic categories and their
        // pages are data here; the only special case is a vanilla category supplied by DefOf.
        static readonly TabSpec[] TabSpecs =
        {
            new TabSpec(PageId.Config, "SlopWorld_Config", "General", () => Icons.Gear,
                () => new ConfigPage()),
            new TabSpec(PageId.Appearance, "SlopWorld_Appearance", "Appearance", () => Icons.Type,
                null),
            new TabSpec(PageId.AppearanceInterface, "SlopWorld_AppearanceInterface", "Interface",
                null, () => new AppearancePage(), PageId.Appearance),
            new TabSpec(PageId.Terminal, "SlopWorld_Terminal", "Terminal", null,
                () => new TerminalPage(), PageId.Appearance),
            new TabSpec(PageId.Audio, "SlopWorld_Audio", "Audio", () => Icons.Bell,
                () => new AudioPage()),
            new TabSpec(PageId.Integrations, "SlopWorld_Integrations", "Integrations",
                () => Icons.Usage, null),
            new TabSpec(PageId.Credentials, "SlopWorld_Credentials", "Credentials", null,
                () => new IntegrationsPage(), PageId.Integrations),
            new TabSpec(PageId.Usage, "SlopWorld_Usage", "Usage", null,
                () => new UsagePage(), PageId.Integrations),
            new TabSpec(PageId.Summaries, "SlopWorld_Summaries", "Summaries", null,
                () => new SummariesPage(), PageId.Integrations),
            new TabSpec(PageId.Sandbox, "SlopWorld_Sandbox", "Sandbox", () => Icons.Shield,
                () => new SandboxPage(SandboxPage.Section.Presets)),
            new TabSpec(PageId.Storage, "SlopWorld_Storage", "Storage", () => Icons.Files,
                () => new StoragePage()),
            new TabSpec(PageId.Commands, "SlopWorld_Commands", "Commands", () => Icons.Terminal,
                null),
            new TabSpec(PageId.CommandDefaults, "SlopWorld_CommandDefaults", "Defaults", null,
                () => new CommandsPage(), PageId.Commands),
            new TabSpec(PageId.CommandPresets, "SlopWorld_CommandPresets", "Presets", null,
                () => new SandboxPage(SandboxPage.Section.Commands), PageId.Commands),
            new TabSpec(PageId.Keyboard, "SlopWorld_Keyboard", "Keyboard", () => Icons.Keyboard,
                () => new KeyBindingsPage()),
            new TabSpec(PageId.RimWorld, "SlopWorld_RimWorld", "RimWorld", () => Icons.RimWorld,
                () => new RimWorldPage()),
            new TabSpec(PageId.About, "SlopWorld_About", "About", () => Icons.Trophy,
                () => new AboutPage()),
        };

        // A child row is indented and iconless; a page-less parent selects its first child.
        class Tab
        {
            public readonly PageId Key;
            public readonly OptionCategoryDef Def;
            public readonly Func<Texture2D> Icon;
            public readonly Func<IOptionPage> PageFactory;
            public readonly Tab Parent;
            public readonly bool Synthetic;

            IOptionPage _page;

            public Tab(PageId key, OptionCategoryDef def, Func<Texture2D> icon,
                Func<IOptionPage> pageFactory, Tab parent, bool synthetic)
            {
                Key = key;
                Def = def;
                Icon = icon;
                PageFactory = pageFactory;
                Parent = parent;
                Synthetic = synthetic;
            }

            public bool HasPage => PageFactory != null;

            public IOptionPage Page
            {
                get
                {
                    if (_page == null && PageFactory != null)
                    {
                        _page = PageFactory();
                        _page.Load();
                    }
                    return _page;
                }
            }

            public void Draw(Rect rect) => Page?.Draw(rect);

            public T PageOf<T>() where T : class, IOptionPage => Page as T;

            public void Reread() => _page?.Load();

            public void Teardown() => _page = null;
        }

        // Column order matches the visible category order, and synthetic entries are added
        // at startup.
        static readonly List<Tab> Column = new List<Tab>();
        static readonly Dictionary<PageId, Tab> Tabs = new Dictionary<PageId, Tab>();

        sealed class RimWorldPage : IOptionPage
        {
            readonly AboutPage _page = new AboutPage();

            public void Load() { }

            public void Draw(Rect rect) => _page.DrawRimWorld(rect);
        }

        public static OptionCategoryDef CategoryFor(PageId key) => TabFor(key)?.Def;

        public static void Install()
        {
            if (Column.Count > 0) return;

            var general = OptionCategoryDefOf.General;
            if (general == null)
            {
                Log.Warning("[SlopWorld] no General option category to sit beside");
                return;
            }

            foreach (var spec in TabSpecs)
                Add(spec, general);

            // Graphics, Interface and Controls are sections of the RimWorld page now, rather
            // than destinations of their own. Keep the vanilla defs available to the renderer,
            // but keep them out of Dialog_Options' category rail.
            HideMergedCategory(OptionCategoryDefOf.Graphics);
            HideMergedCategory(OptionCategoryDefOf.Interface);
            HideMergedCategory(OptionCategoryDefOf.Controls);

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

        static void HideMergedCategory(OptionCategoryDef category)
        {
            if (category != null) category.isDev = true;
        }

        static Tab Add(TabSpec spec, OptionCategoryDef general)
        {
            var def = spec.Existing != null
                ? spec.Existing()
                : MakeDef(spec.DefName, spec.Label, general);
            if (def == null) return null;

            var parent = spec.Parent.HasValue ? TabFor(spec.Parent.Value) : null;
            var tab = new Tab(spec.Key, def, spec.Icon, spec.PageFactory, parent,
                spec.Existing == null);
            Column.Add(tab);
            Tabs.Add(tab.Key, tab);
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

        static Tab TabFor(PageId key)
        {
            Tab tab;
            return Tabs.TryGetValue(key, out tab) ? tab : null;
        }

        // What a press on a row selects. A tab with no page of its own is a heading with
        // pages under it, so the press opens the first of them.
        static Tab Target(Tab tab) =>
            tab == null || tab.HasPage ? tab : FirstChild(tab);

        // ---------------------------------------------------------------- the pages

        public static void OpenNewSandboxPreset()
        {
            var tab = TabFor(PageId.Sandbox);
            var page = tab?.PageOf<SandboxPage>();
            if (page == null) return;
            page.NewPreset();
            OpenCategory(tab.Def);
        }

        public static void OpenNewCommand()
        {
            var tab = TabFor(PageId.CommandPresets);
            var page = tab?.PageOf<SandboxPage>();
            if (page == null) return;
            page.NewCommand();
            OpenCategory(tab.Def);
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
            TerminalWindow.ToggleContent(() => new OptionsView(_lastCategory
                ?? CategoryFor(PageId.Config)));

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

        // The jukebox menu's Settings row opens our mixer directly.
        public static void OpenAudioTab()
        {
            var category = CategoryFor(PageId.Audio);
            var v = TerminalWindow.ShowingAs<OptionsView>();
            if (v == null)
            {
                TerminalWindow.OpenContent(new OptionsView(category));
                return;
            }
            if (category != null) v.Category = category;
        }

        // A save is a write of the *whole* file - every page here PUTs the sections it knows
        // about - so a page holding a copy read before that write would put the old figures
        // back the next time its own Save was pressed. Both re-read instead, including the
        // one that just saved, which costs a request and closes the hole.
        public static void Reread()
        {
            foreach (var tab in Column) tab.Reread();
        }

        // Drop page instances and persist settings when the view/window closes.
        public static void Teardown()
        {
            foreach (var tab in Column) tab.Teardown();
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
        static float RowH => Mathf.Round(SlopWidgets.LineH * 1.4f);
        static float NestedRowH => Mathf.Round(SlopWidgets.LineH * 1.15f);
        static float Pitch => RowH + SlopWidgets.GapXS;
        static float NestedPitch => NestedRowH + SlopWidgets.GapXS;

        // Limit the icon box to the row height.
        static float IconBox => Mathf.Min(18f, RowH - 6f);
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
                var tab = Target(TabOf(category));
                if (tab == null || !tab.HasPage) return true;

                tab.Draw(inRect);
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
                    __instance.selectedCategory = CategoryFor(PageId.Config);
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
