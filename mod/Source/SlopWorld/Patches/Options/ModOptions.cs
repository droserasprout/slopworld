using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SlopWorld
{
    // Place options in the content area beside the category column. Limit page width.
    // Base game categories use window coordinates, so center the content with a GUI group.
    // See OptionsView and ChromeShift.
    public static partial class ModOptions
    {
        // OptionsView owns the content band, width, and button space.
        // This file owns categories and pages.

        public enum PageId
        {
            Config,
            Commands,
            CommandDefaults,
            CommandBinaries,
            AppPresets,
            Storage,
            Appearance,
            AppearanceInterface,
            Terminal,
            AppearanceStatusbar,
            AppearanceSidebar,
            Audio,
            Integrations,
            Credentials,
            Usage,
            Agents,
            Summaries,
            Workers,
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

        // Define categories and page factories in display order.
        // Existing base game categories use their DefOf definitions.
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
            new TabSpec(PageId.AppearanceStatusbar, "SlopWorld_AppearanceStatusbar", "Status bar",
                null, () => new StatusbarPage(), PageId.Appearance),
            new TabSpec(PageId.AppearanceSidebar, "SlopWorld_AppearanceSidebar", "Sidebar",
                null, () => new SidebarPage(), PageId.Appearance),
            new TabSpec(PageId.Integrations, "SlopWorld_Integrations", "Integrations",
                () => Icons.Link, null),
            new TabSpec(PageId.Credentials, "SlopWorld_Credentials", "Credentials", null,
                () => new IntegrationsPage(), PageId.Integrations),
            new TabSpec(PageId.Usage, "SlopWorld_Usage", "Usage", null,
                () => new UsagePage(), PageId.Integrations),
            new TabSpec(PageId.Agents, "SlopWorld_Agents", "Agents", () => Icons.Agents,
                null),
            new TabSpec(PageId.Summaries, "SlopWorld_Summaries", "Summaries", null,
                () => new SummariesPage(), PageId.Agents),
            new TabSpec(PageId.Workers, "SlopWorld_Workers", "Workers", null,
                () => new WorkersPage(), PageId.Agents),
            new TabSpec(PageId.Commands, "SlopWorld_Commands", "Commands", () => Icons.Terminal,
                null),
            new TabSpec(PageId.CommandDefaults, "SlopWorld_CommandDefaults", "Defaults", null,
                () => new CommandsPage(), PageId.Commands),
            new TabSpec(PageId.AppPresets, "SlopWorld_AppPresets", "Apps", null,
                () => new SandboxPage(SandboxPage.Section.AppPresets), PageId.Commands),
            new TabSpec(PageId.CommandBinaries, "SlopWorld_CommandBinaries", "Binaries", null,
                () => new BinariesPage(), PageId.Commands),
            new TabSpec(PageId.Sandbox, "SlopWorld_Sandbox", "Sandbox", () => Icons.Shield,
                () => new SandboxPage(SandboxPage.Section.SandboxPresets)),
            new TabSpec(PageId.Keyboard, "SlopWorld_Keyboard", "Keyboard", () => Icons.Keyboard,
                () => new KeyBindingsPage()),
            new TabSpec(PageId.Storage, "SlopWorld_Storage", "Storage", () => Icons.Files,
                () => new StoragePage()),
            new TabSpec(PageId.Audio, "SlopWorld_Audio", "Audio", () => Icons.Bell,
                () => new AudioPage()),
            new TabSpec(PageId.RimWorld, "SlopWorld_RimWorld", "RimWorld", () => Icons.RimWorld,
                () => new RimWorldPage()),
            new TabSpec(PageId.About, "SlopWorld_About", "About", () => Icons.Trophy,
                () => new AboutPage()),
        };

        // Indent child rows without icons. A parent without a page selects its first child.
        class Tab
        {
            public readonly PageId Key;
            public readonly OptionCategoryDef Def;
            public readonly Func<Texture2D> Icon;
            public readonly Func<IOptionPage> PageFactory;
            public readonly Tab Parent;
            public readonly bool Synthetic;

            IOptionPage _page;
            readonly ScrollWheelRouter _wheel = new ScrollWheelRouter();
            FieldLifetime _fields = new FieldLifetime();

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

            public void Draw(Rect rect)
            {
                var page = Page;
                if (page == null) return;
                var window = Find.WindowStack?.currentlyDrawnWindow;
                bool input = window == null || Find.WindowStack.GetsInput(window);
                bool capturing = page is KeyBindingsPage keys && keys.Listening;
                _wheel.Draw(rect, () =>
                {
                    using (FieldLifetimeScope.Push(_fields))
                    using (new FieldFocusScope(_fields, input && !capturing))
                        page.Draw(rect);
                });
            }

            public T PageOf<T>() where T : class, IOptionPage => Page as T;

            public void Teardown()
            {
                _wheel.Invalidate();
                _fields.Cancel();
                _fields = new FieldLifetime();
                if (_page is IDisposable disposable) disposable.Dispose();
                _page = null;
            }
        }

        // Store categories in display order. Add generated definitions at startup.
        static readonly List<Tab> Column = new List<Tab>();
        static readonly Dictionary<PageId, Tab> Tabs = new Dictionary<PageId, Tab>();

        sealed class RimWorldPage : IOptionPage
        {
            readonly AboutPage _page = new AboutPage();

            public void Load() { }

            public void Draw(Rect rect) => _page.DrawRimWorld(rect);
        }

        public static OptionCategoryDef CategoryFor(PageId key) => TabFor(key)?.Def;

        // Let the Keyboard page capture input before terminal shortcuts when it waits for a binding.
        // This permits function keys to reach KeyBindingsPage.CaptureKey().
        public static bool KeyboardCaptureActive
        {
            get
            {
                var view = TerminalWindow.ShowingAs<OptionsView>();
                if (view == null || view.Category != CategoryFor(PageId.Keyboard)) return false;
                return TabFor(PageId.Keyboard)?.PageOf<KeyBindingsPage>()?.Listening == true;
            }
        }

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

            // Show Graphics, Interface, and Controls as sections of the RimWorld page.
            // Retain their definitions for rendering but hide their separate category rows.
            HideMergedCategory(OptionCategoryDefOf.Graphics);
            HideMergedCategory(OptionCategoryDefOf.Interface);
            HideMergedCategory(OptionCategoryDefOf.Controls);

            foreach (var tab in Column)
                if (tab.Synthetic) DefDatabase<OptionCategoryDef>.Add(tab.Def);

            // Move mod categories to the start of the database in display order. Leave other categories after them.
            var all = DefDatabase<OptionCategoryDef>.AllDefsListForReading;
            for (int i = 0; i < Column.Count; i++)
            {
                all.Remove(Column[i].Def);
                all.Insert(i, Column[i].Def);
            }

            // Hide the duplicate Audio category because the mod Audio page provides its controls. Retain the definition.
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

        // Reuse the General content pack so Dialog_Options accepts the generated category.
        // Custom drawing supplies rows and icons without using texPath.
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

        // Return no child for a null parent. Otherwise, the search would match a top-level tab.
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

        // Select the tab page, or its first child if the tab has no page.
        static Tab Target(Tab tab) =>
            tab == null || tab.HasPage ? tab : FirstChild(tab);

        // Page navigation.

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
            var tab = TabFor(PageId.AppPresets);
            var page = tab?.PageOf<SandboxPage>();
            if (page == null) return;
            page.NewCommand();
            OpenCategory(tab.Def);
        }

        public static void OpenSandboxPreset(string name)
        {
            var tab = TabFor(PageId.Sandbox);
            var page = tab?.PageOf<SandboxPage>();
            if (page == null) return;
            page.SelectPreset(name);
            OpenCategory(tab.Def);
        }

        public static void OpenAppPreset(string name)
        {
            var tab = TabFor(PageId.AppPresets);
            var page = tab?.PageOf<SandboxPage>();
            if (page == null) return;
            page.SelectCommand(name);
            OpenCategory(tab.Def);
        }

        // Remember the last category because each toggle rebuilds the view and reloads configuration.
        static OptionCategoryDef _lastCategory;

        // OptionsView.Closed calls this before closing the view.
        public static void Remember(OptionCategoryDef category)
        {
            if (category != null) _lastCategory = category;
        }

        // Toggle the options content view from the Config button.
        // Reopen the last category, or Config on first use.
        // Use a content view so the surrounding interface can still receive input.
        public static void Toggle() =>
            TerminalWindow.ToggleContent(() => new OptionsView(_lastCategory
                ?? CategoryFor(PageId.Config)));

        // Open the category in the current options view, or create a view if necessary.
        // Palette entries and page links use this method.
        public static void OpenCategory(OptionCategoryDef category)
        {
            if (category == null) return;

            var target = Target(TabOf(category));
            if (target != null) category = target.Def;

            var v = TerminalWindow.ShowingAs<OptionsView>();
            if (v == null) TerminalWindow.OpenContent(new OptionsView(category));
            else v.Category = category;
        }

        // Open the Audio page from the jukebox Settings action.
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

        // Release page instances and save settings when the view or window closes.
        public static void Teardown()
        {
            foreach (var tab in Column) tab.Teardown();
            ModEntry.Instance.settings.Write();
        }

    }
}
