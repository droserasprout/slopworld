using System;
using System.Collections.Generic;

namespace SlopWorld
{
    public sealed class SidebarTabHandlers
    {
        public Action Draw = Noop;
        public Action Click = Noop;
        public Action<SidebarTabActionContext> DrawActions = NoopActions;
        public Action Refresh = Noop;
        public Action FilterChanged = Noop;
        public Action<bool> SetAllFolds = NoFolds;
        public Func<bool> AllFolded = NeverFolded;
        public Action Close = Noop;
        public Action Entered = Noop;
        public Action Reselected = Noop;

        static void Noop() { }
        static void NoopActions(SidebarTabActionContext context) { }
        static void NoFolds(bool folded) { }
        static bool NeverFolded() => false;
    }

    public sealed class SidebarTabDefinition
    {
        public SidebarTab Tab { get; }
        public string PersistedName { get; }
        public string IconKey { get; }
        public string Tooltip { get; }
        public bool HasActions { get; }
        public bool CanFold { get; }
        public bool CanToggleDotfiles { get; }

        public Action Draw { get; }
        public Action Click { get; }
        public Action<SidebarTabActionContext> DrawActions { get; }
        public Action Refresh { get; }
        public Action FilterChanged { get; }
        public Action<bool> SetAllFolds { get; }
        public Func<bool> AllFolded { get; }
        public Action Close { get; }
        public Action Entered { get; }
        public Action Reselected { get; }

        public SidebarTabDefinition(
            SidebarTab tab, string persistedName, string iconKey, string tooltip,
            bool hasActions, bool canFold, bool canToggleDotfiles,
            SidebarTabHandlers handlers)
        {
            if (string.IsNullOrEmpty(persistedName))
                throw new ArgumentException("A sidebar tab needs a persisted name.", nameof(persistedName));
            if (string.IsNullOrEmpty(iconKey))
                throw new ArgumentException("A sidebar tab needs an icon key.", nameof(iconKey));
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));

            Tab = tab;
            PersistedName = persistedName;
            IconKey = iconKey;
            Tooltip = tooltip ?? string.Empty;
            HasActions = hasActions;
            CanFold = canFold;
            CanToggleDotfiles = canToggleDotfiles;
            Draw = handlers.Draw ?? (() => { });
            Click = handlers.Click ?? (() => { });
            DrawActions = handlers.DrawActions ?? (_ => { });
            Refresh = handlers.Refresh ?? (() => { });
            FilterChanged = handlers.FilterChanged ?? (() => { });
            SetAllFolds = handlers.SetAllFolds ?? (_ => { });
            AllFolded = handlers.AllFolded ?? (() => false);
            Close = handlers.Close ?? (() => { });
            Entered = handlers.Entered ?? (() => { });
            Reselected = handlers.Reselected ?? (() => { });
        }
    }

    // Stable navigation order, persisted IDs and capabilities are shared by registration and tests.
    // AgentSidebar supplies game-bound handlers; the registry owns lookup and activation.
    public static class SidebarTabCatalog
    {
        public static readonly IReadOnlyList<SidebarTab> Order = Array.AsReadOnly(new[] {
            SidebarTab.Agents,
            SidebarTab.Files,
            SidebarTab.Git,
            SidebarTab.Search,
            SidebarTab.Tasks,
            SidebarTab.Library,
        });

        public static SidebarTabRegistry CreateRegistry(Func<SidebarTab, SidebarTabHandlers> handlers)
        {
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));
            var definitions = new SidebarTabDefinition[Order.Count];
            for (int i = 0; i < Order.Count; i++) definitions[i] = Definition(Order[i], handlers(Order[i]));
            return new SidebarTabRegistry(definitions);
        }

        public static SidebarTabDefinition Definition(SidebarTab tab, SidebarTabHandlers handlers)
        {
            switch (tab)
            {
                case SidebarTab.Agents: return new SidebarTabDefinition(tab, "agents", "agents",
                    "Agents: view all sessions, grouped by project.",
                    hasActions: true, canFold: true, canToggleDotfiles: false, handlers: handlers);
                case SidebarTab.Files: return new SidebarTabDefinition(tab, "files", "files",
                    "Files: browse each project's files in a tree.",
                    hasActions: true, canFold: true, canToggleDotfiles: true, handlers: handlers);
                case SidebarTab.Git: return new SidebarTabDefinition(tab, "git", "git",
                    "Git: view changes since the last commit in each working tree.",
                    hasActions: true, canFold: true, canToggleDotfiles: false, handlers: handlers);
                case SidebarTab.Search: return new SidebarTabDefinition(tab, "search", "search",
                    "Search: find text in any project.",
                    hasActions: true, canFold: false, canToggleDotfiles: true, handlers: handlers);
                case SidebarTab.Tasks: return new SidebarTabDefinition(tab, "tasks", "tasks",
                    "Tasks: view and manage delegated tasks.",
                    hasActions: true, canFold: false, canToggleDotfiles: false, handlers: handlers);
                case SidebarTab.Library: return new SidebarTabDefinition(tab, "library", "library",
                    "Library: manage saved items, projects, worktrees, and presets. " +
                    "Saved items include templates, prompts, commands, breadcrumbs, and file actions.",
                    hasActions: true, canFold: true, canToggleDotfiles: false, handlers: handlers);
                default: throw new ArgumentOutOfRangeException(nameof(tab));
            }
        }
    }

    public sealed class SidebarTabRegistry
    {
        readonly System.Collections.ObjectModel.ReadOnlyCollection<SidebarTabDefinition> _definitions;
        readonly Dictionary<SidebarTab, SidebarTabDefinition> _byTab =
            new Dictionary<SidebarTab, SidebarTabDefinition>();
        readonly Dictionary<string, SidebarTabDefinition> _byName =
            new Dictionary<string, SidebarTabDefinition>(StringComparer.Ordinal);

        public SidebarTabRegistry(params SidebarTabDefinition[] definitions)
        {
            if (definitions == null || definitions.Length == 0)
                throw new ArgumentException("A sidebar tab registry needs definitions.", nameof(definitions));

            _definitions = Array.AsReadOnly((SidebarTabDefinition[])definitions.Clone());
            foreach (var definition in _definitions)
            {
                if (definition == null)
                    throw new ArgumentException("A tab definition cannot be null.", nameof(definitions));
                if (_byTab.ContainsKey(definition.Tab))
                    throw new ArgumentException("A tab cannot have duplicate definitions.", nameof(definitions));
                if (_byName.ContainsKey(definition.PersistedName))
                    throw new ArgumentException("A tab cannot have duplicate persisted names.", nameof(definitions));
                _byTab.Add(definition.Tab, definition);
                _byName.Add(definition.PersistedName, definition);
            }

            if (!_byTab.TryGetValue(SidebarTab.Agents, out _))
                throw new ArgumentException("The Agents tab is the unknown-tab fallback.", nameof(definitions));
        }

        public IEnumerable<SidebarTabDefinition> Definitions => _definitions;

        public SidebarTabDefinition For(SidebarTab tab)
        {
            return _byTab.TryGetValue(tab, out var definition)
                ? definition
                : _byTab[SidebarTab.Agents];
        }

        public SidebarTabDefinition FromPersisted(string value)
        {
            return value != null && _byName.TryGetValue(value, out var definition)
                ? definition
                : _byTab[SidebarTab.Agents];
        }
    }

    public static class SidebarTabActivation
    {
        public static bool Activate(
            SidebarTabDefinition current, SidebarTabDefinition target,
            IEnumerable<SidebarTabDefinition> definitions,
            Action closeMenus, Action persist)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (closeMenus == null) throw new ArgumentNullException(nameof(closeMenus));
            if (persist == null) throw new ArgumentNullException(nameof(persist));

            closeMenus();
            if (current.Tab == target.Tab)
            {
                target.Reselected();
                return false;
            }

            foreach (var definition in definitions)
                if (definition != null && definition.Tab != target.Tab)
                    definition.Close();

            persist();
            target.Entered();
            return true;
        }
    }
}
