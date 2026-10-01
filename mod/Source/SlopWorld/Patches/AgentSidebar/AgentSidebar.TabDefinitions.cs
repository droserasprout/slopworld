using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static partial class AgentSidebar
    {
        static readonly SidebarTabRegistry TabRegistry = BuildTabRegistry();

        static SidebarTabDefinition CurrentDefinition =>
            TabRegistry.FromPersisted(Settings.SidebarTab);

        static SidebarTabRegistry BuildTabRegistry()
        {
            return new SidebarTabRegistry(
                new SidebarTabDefinition(
                    SidebarTab.Agents, "agents", "agents",
                    "Agents: view all sessions, grouped by project.",
                    hasActions: true, canFold: true, canToggleDotfiles: false,
                    handlers: new SidebarTabHandlers
                    {
                        Draw = DrawAgentTab,
                        Click = Menus,
                        DrawActions = DrawAgentActions,
                        Refresh = () => SessionHub.Instance.SessionStore.Refresh(),
                        SetAllFolds = SetAgentFolds,
                        AllFolded = AllAgentsFolded,
                    }),
                new SidebarTabDefinition(
                    SidebarTab.Files, "files", "files",
                    "Files: browse each project's files in a tree.",
                    hasActions: true, canFold: true, canToggleDotfiles: true,
                    handlers: new SidebarTabHandlers
                    {
                        Draw = DrawFilesView,
                        Click = ClickFilesView,
                        DrawActions = DrawFilesActions,
                        Refresh = FilesView.Reload,
                        SetAllFolds = FilesView.SetAllFolded,
                        AllFolded = () => FilesView.AllFolded,
                        Close = () =>
                        {
                            EndFilesDivider();
                            GitView.CancelPendingDiff();
                            FilesView.ClearFocus();
                        },
                        Entered = () =>
                        {
                            FilesView.Entered();
                            GitView.Entered();
                        },
                        Reselected = () =>
                        {
                            FilesView.Entered();
                            GitView.Refresh();
                        },
                    }),
                new SidebarTabDefinition(
                    SidebarTab.Git, "git", "git",
                    "Git: view changes since the last commit in each working tree.",
                    hasActions: true, canFold: true, canToggleDotfiles: false,
                    handlers: new SidebarTabHandlers
                    {
                        Draw = DrawGitView,
                        Click = ClickGitView,
                        DrawActions = DrawGitActions,
                        Refresh = GitView.Refresh,
                        FilterChanged = GitView.Refresh,
                        SetAllFolds = GitView.SetAllFolded,
                        AllFolded = () => GitView.AllFolded,
                        Close = () =>
                        {
                            EndFilesDivider();
                            GitView.CancelPendingDiff();
                        },
                        Entered = GitView.Refresh,
                        Reselected = GitView.Refresh,
                    }),
                new SidebarTabDefinition(
                    SidebarTab.Search, "search", "search",
                    "Search: find text in any project.",
                    hasActions: true, canFold: false, canToggleDotfiles: true,
                    handlers: new SidebarTabHandlers
                    {
                        Draw = () => SearchView.Draw(Body),
                        Click = SearchView.Clicks,
                        DrawActions = DrawSearchActions,
                        Refresh = SearchView.Search,
                        FilterChanged = SidebarScopes.Update,
                        Close = SearchView.Closed,
                        Entered = SearchView.Entered,
                    }),
                new SidebarTabDefinition(
                    SidebarTab.Tasks, "tasks", "tasks",
                    "Tasks: view and manage delegated tasks.",
                    hasActions: true, canFold: false, canToggleDotfiles: false,
                    handlers: new SidebarTabHandlers
                    {
                        Draw = () => TasksView.Draw(Body),
                        Click = TasksView.Clicks,
                        DrawActions = DrawTasksActions,
                        Refresh = () => SessionHub.Instance.TaskStore.Refresh(fail: UiLayout.Fail),
                        Entered = () => SessionHub.Instance.TaskStore.Refresh(fail: UiLayout.Fail),
                        Reselected = () => SessionHub.Instance.TaskStore.Refresh(fail: UiLayout.Fail),
                    }),
                new SidebarTabDefinition(
                    SidebarTab.Library, "library", "library",
                    "Library: manage saved items, projects, worktrees, and presets. " +
                    "Saved items include templates, prompts, commands, breadcrumbs, and file actions.",
                    hasActions: true, canFold: true, canToggleDotfiles: false,
                    handlers: new SidebarTabHandlers
                    {
                        Draw = () => LibraryView.Draw(Body),
                        Click = LibraryView.Clicks,
                        Close = LibraryView.Closed,
                        DrawActions = DrawLibraryActions,
                        Refresh = RefreshLibrary,
                        Reselected = RefreshLibrary,
                        // Fetch on entry as well, including when socket updates are unavailable.
                        Entered = () => LibraryView.Refresh(),
                        SetAllFolds = LibraryView.SetAllFolded,
                        AllFolded = () => LibraryView.AllFolded,
                    }));
        }

        static void DrawFilesView() => DrawReaderView(SidebarTab.Files);

        static void DrawGitView() => DrawReaderView(SidebarTab.Git);

        static void DrawReaderView(SidebarTab tab)
        {
            var body = Body;
            PrepareRouted();
            var split = FilesSplit(body);
            HandleFilesDivider(body, split);
            split = FilesSplit(body);
            DrawRouted(ToRect(split.Upper), Interaction.FilesRoutedScroll);
            if (tab == SidebarTab.Files)
                FilesView.Draw(ToRect(split.Lower), !Interaction.FilesDividerDragging);
            else
                GitView.Draw(ToRect(split.Lower), !Interaction.FilesDividerDragging);
            DrawFilesDivider(split, body);
        }

        static SidebarFilesSplitGeometry FilesSplit(Rect body) =>
            SidebarFilesSplitGeometry.Arrange(
                new UiLayoutRect(body.x, body.y, body.width, body.height),
                Layout.Routed.Count > 0, Settings.SidebarFilesOpenFraction,
                GhostH, UiTheme.TinyRowH, FilesDividerH);

        static Rect ToRect(UiLayoutRect rect) =>
            new Rect(rect.X, rect.Y, rect.Width, rect.Height);

        static void ClickFilesView()
        {
            if (Interaction.FilesDividerInput || Interaction.FilesDividerDragging) return;
            if (!ClickRouted()) FilesView.Clicks();
        }

        static void ClickGitView()
        {
            if (Interaction.FilesDividerInput || Interaction.FilesDividerDragging) return;
            if (!ClickRouted()) GitView.Clicks();
        }

        static void SetAgentFolds(bool folded)
        {
            Projects.SetFolded(Layout.Order, folded);
        }

        static bool AllAgentsFolded()
        {
            return Layout.Order.Count > 0 && Layout.Order.TrueForAll(Folded.Contains);
        }

        static Rect ActionRect(SidebarTabActionContext context) =>
            new Rect(context.X, context.Y, context.Width, context.Height);

        static void DrawAgentActions(SidebarTabActionContext context)
        {
            var r = ActionRect(context);
            bool folded = AllFolded();
            Tab(r, folded ? TexButton.Reveal : TexButton.Collapse, folded,
                folded ? "Unfold all." : "Fold all.", () => SetAllFolds(!folded));
            DrawAgentVisibilityAction(context.ShiftX(-(TabIcon + 3f)));
        }

        static void DrawAgentVisibilityAction(SidebarTabActionContext context)
        {
            var r = ActionRect(context);
            _agentVisibilityRect = r;
            Tab(r, Icons.Hidden, StatusFiltering,
                StatusFiltering
                    ? $"Showing {StatusFilterLabel} agents. Click to change the status filter."
                    : "All agents shown. Click to choose agent statuses.",
                OpenAgentVisibilityMenu);
        }

        static void DrawFilesActions(SidebarTabActionContext context)
        {
            var r = ActionRect(context);
            DrawFoldAction(ref r);
            DrawVisibilityAction(r);
        }

        static void DrawSearchActions(SidebarTabActionContext context)
        {
            DrawVisibilityAction(ActionRect(context));
        }

        static void DrawVisibilityAction(Rect r)
        {
            bool active = Settings.SidebarShowHidden || Settings.SidebarShowGitignored;
            _visibilityRect = r;
            Tab(r, Icons.Hidden, active,
                active
                    ? "Showing dotfiles or Git-ignored files. Click to change the filters."
                    : "All files shown. Click to choose which files to show.",
                OpenVisibilityMenu);
        }

        static void DrawGitActions(SidebarTabActionContext context)
        {
            var r = ActionRect(context);
            DrawFoldAction(ref r);
            Tab(r, Icons.Refresh, false,
                "Refresh Git status for every working tree.", GitView.Refresh);
        }

        static void RefreshLibrary() => LibraryView.Refresh(UiLayout.Fail);

        static void DrawLibraryActions(SidebarTabActionContext context)
        {
            var r = ActionRect(context);
            DrawFoldAction(ref r);
            DrawLibraryProjectFilterAction(ref r);
            Tab(r, Icons.Refresh, false, "Refresh the Library data.",
                RefreshLibrary);
        }

        static void DrawLibraryProjectFilterAction(ref Rect r)
        {
            var anchor = r;
            Tab(r, Icons.Filter, LibraryView.ProjectFiltering,
                LibraryView.ProjectFiltering
                    ? $"Showing {LibraryView.ProjectFilterLabel} in Library. Click to change the project filter."
                    : "Library shows every project. Click to filter the list.",
                () => LibraryView.OpenProjectFilterMenu(anchor));
            r.x -= TabIcon + 3f;
        }

        static void DrawTasksActions(SidebarTabActionContext context)
        {
            var r = ActionRect(context);
            Tab(r, Icons.Refresh, false,
                "Refresh the task list.",
                () => SessionHub.Instance.TaskStore.Refresh(fail: UiLayout.Fail));
            r.x -= TabIcon + 3f;
            TasksView.FilterButton(r);
        }

        static void DrawFoldAction(ref Rect r)
        {
            bool folded = AllFolded();
            Tab(r, folded ? TexButton.Reveal : TexButton.Collapse, folded,
                folded ? "Unfold all." : "Fold all.", () => SetAllFolds(!folded));
            r.x -= TabIcon + 3f;
        }
    }
}
