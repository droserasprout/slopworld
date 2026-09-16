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
    public static partial class ModOptions
    {
        // The band, its width and the row taken off the foot for the OK button are
        // OptionsView's: they are about the shape the pages are drawn in, and this file is
        // the column of categories and the pages themselves.

        public enum PageId
        {
            Config,
            Commands,
            CommandDefaults,
            CommandBinaries,
            CommandPresets,
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
            Templates,
            Summaries,
            Instructions,
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
            new TabSpec(PageId.Templates, "SlopWorld_Templates", "Templates", null,
                () => new TemplateCatalogPage(), PageId.Agents),
            new TabSpec(PageId.Summaries, "SlopWorld_Summaries", "Summaries", null,
                () => new SummariesPage(), PageId.Agents),
            new TabSpec(PageId.Instructions, "SlopWorld_Instructions", "Instructions", null,
                () => new InstructionsPage(), PageId.Agents),
            new TabSpec(PageId.Workers, "SlopWorld_Workers", "Workers", null,
                () => new WorkersPage(), PageId.Agents),
            new TabSpec(PageId.Commands, "SlopWorld_Commands", "Commands", () => Icons.Terminal,
                null),
            new TabSpec(PageId.CommandDefaults, "SlopWorld_CommandDefaults", "Defaults", null,
                () => new CommandsPage(), PageId.Commands),
            new TabSpec(PageId.CommandPresets, "SlopWorld_CommandPresets", "Presets", null,
                () => new SandboxPage(SandboxPage.Section.Commands), PageId.Commands),
            new TabSpec(PageId.CommandBinaries, "SlopWorld_CommandBinaries", "Binaries", null,
                () => new BinariesPage(), PageId.Commands),
            new TabSpec(PageId.Sandbox, "SlopWorld_Sandbox", "Sandbox", () => Icons.Shield,
                () => new SandboxPage(SandboxPage.Section.Presets)),
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
                using (FieldLifetimeScope.Push(_fields))
                using (new FieldFocusScope(_fields, input && !capturing))
                    page.Draw(rect);
            }

            public T PageOf<T>() where T : class, IOptionPage => Page as T;

            public void Teardown()
            {
                _fields.Cancel();
                _fields = new FieldLifetime();
                if (_page is IDisposable disposable) disposable.Dispose();
                _page = null;
            }
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

        // The in-game options view gives the terminal chrome first chance at key events.
        // While the Keyboard page is waiting for a binding, that chance must be yielded so
        // F-keys and other chrome bindings can reach KeyBindingsPage.CaptureKey().
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

        // Drop page instances and persist settings when the view/window closes.
        public static void Teardown()
        {
            foreach (var tab in Column) tab.Teardown();
            ModEntry.Instance.settings.Write();
        }

    }
}
