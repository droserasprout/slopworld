using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // One definition holds each tab's fixed metadata and game-free callback surface.
    // Sidebar binds game-owned behavior once when constructing its registry.
    public sealed class SidebarTabDefinition
    {
        public SidebarTab Tab { get; }
        public string PersistedName { get; }
        public string IconKey { get; }
        public string Tooltip { get; }
        public bool HasActions { get; }
        public bool CanFold { get; }
        public bool CanToggleDotfiles { get; }

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

        internal SidebarTabDefinition(
            SidebarTab tab,
            string persistedName,
            string iconKey,
            string tooltip,
            bool hasActions,
            bool canFold,
            bool canToggleDotfiles
        )
        {
            Tab = tab;
            PersistedName = persistedName;
            IconKey = iconKey;
            Tooltip = tooltip;
            HasActions = hasActions;
            CanFold = canFold;
            CanToggleDotfiles = canToggleDotfiles;
        }
    }

    // This is the fixed sidebar catalog, not an extensible registration mechanism.
    // Array order governs both navigation and activation cleanup; Agents is the fallback.
    public sealed class SidebarTabRegistry
    {
        public IReadOnlyList<SidebarTabDefinition> Definitions { get; }

        public SidebarTabRegistry(Action<SidebarTabDefinition> configure = null)
        {
            Definitions = Array.AsReadOnly(
                new[]
                {
                    new SidebarTabDefinition(
                        SidebarTab.Agents,
                        "agents",
                        "agents",
                        "Agents: view all sessions, grouped by project.",
                        hasActions: true,
                        canFold: true,
                        canToggleDotfiles: false
                    ),
                    new SidebarTabDefinition(
                        SidebarTab.Files,
                        "files",
                        "files",
                        "Files: browse each project's files in a tree.",
                        hasActions: true,
                        canFold: true,
                        canToggleDotfiles: true
                    ),
                    new SidebarTabDefinition(
                        SidebarTab.Git,
                        "git",
                        "git",
                        "Git: view changes since the last commit in each working tree.",
                        hasActions: true,
                        canFold: true,
                        canToggleDotfiles: false
                    ),
                    new SidebarTabDefinition(
                        SidebarTab.Search,
                        "search",
                        "search",
                        "Search: find text in any project.",
                        hasActions: true,
                        canFold: true,
                        canToggleDotfiles: false
                    ),
                    new SidebarTabDefinition(
                        SidebarTab.Tasks,
                        "tasks",
                        "tasks",
                        "Tasks: view and manage delegated tasks.",
                        hasActions: true,
                        canFold: false,
                        canToggleDotfiles: false
                    ),
                    new SidebarTabDefinition(
                        SidebarTab.Library,
                        "library",
                        "library",
                        "Library: manage saved items, projects, worktrees, and presets. "
                            + "Saved items include templates, prompts, commands, breadcrumbs, and file actions.",
                        hasActions: true,
                        canFold: true,
                        canToggleDotfiles: false
                    ),
                }
            );
            if (configure != null)
                foreach (var definition in Definitions)
                    configure(definition);
        }

        public SidebarTabDefinition For(SidebarTab tab)
        {
            foreach (var definition in Definitions)
                if (definition.Tab == tab)
                    return definition;
            return Definitions[0];
        }

        public SidebarTabDefinition FromPersisted(string value)
        {
            foreach (var definition in Definitions)
                if (string.Equals(definition.PersistedName, value, StringComparison.Ordinal))
                    return definition;
            return Definitions[0];
        }
    }

    public static class SidebarTabActivation
    {
        public static bool Activate(
            SidebarTabDefinition current,
            SidebarTabDefinition target,
            IEnumerable<SidebarTabDefinition> definitions,
            Action closeMenus,
            Action persist
        )
        {
            if (current == null)
                throw new ArgumentNullException(nameof(current));
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));
            if (closeMenus == null)
                throw new ArgumentNullException(nameof(closeMenus));
            if (persist == null)
                throw new ArgumentNullException(nameof(persist));

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
