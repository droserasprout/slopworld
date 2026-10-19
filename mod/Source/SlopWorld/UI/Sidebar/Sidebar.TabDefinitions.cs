using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static partial class Sidebar
    {
        static readonly SidebarTabRegistry TabRegistry = BuildTabRegistry();

        static SidebarTabDefinition CurrentDefinition =>
            TabRegistry.FromPersisted(Settings.SidebarTab);

        static SidebarTabRegistry BuildTabRegistry() =>
            SidebarTabCatalog.CreateRegistry(HandlersFor);

        static SidebarTabHandlers HandlersFor(SidebarTab tab)
        {
            switch (tab)
            {
                case SidebarTab.Agents:
                    return new SidebarTabHandlers
                    {
                        Draw = DrawAgentTab,
                        Click = Menus,
                        DrawActions = DrawAgentActions,
                        Refresh = () => SessionHub.Instance.SessionStore.Refresh(),
                        SetAllFolds = SetAgentFolds,
                        AllFolded = AllAgentsFolded,
                    };
                case SidebarTab.Files:
                    return new SidebarTabHandlers
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
                    };
                case SidebarTab.Git:
                    return new SidebarTabHandlers
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
                    };
                case SidebarTab.Search:
                    return new SidebarTabHandlers
                    {
                        Draw = () => SearchView.Draw(Body),
                        Click = SearchView.Clicks,
                        DrawActions = DrawSearchActions,
                        Refresh = SearchView.Search,
                        SetAllFolds = SearchView.SetAllFolded,
                        AllFolded = () => SearchView.AllFolded,
                        FilterChanged = SidebarScopes.Update,
                        Close = SearchView.Closed,
                        Entered = SearchView.Entered,
                    };
                case SidebarTab.Tasks:
                    return new SidebarTabHandlers
                    {
                        Draw = () => TasksView.Draw(Body),
                        Click = TasksView.Clicks,
                        DrawActions = DrawTasksActions,
                        Refresh = () => SessionHub.Instance.TaskStore.Refresh(fail: UiLayout.Fail),
                        Entered = () => SessionHub.Instance.TaskStore.Refresh(fail: UiLayout.Fail),
                        Reselected = () =>
                            SessionHub.Instance.TaskStore.Refresh(fail: UiLayout.Fail),
                    };
                case SidebarTab.Library:
                    return new SidebarTabHandlers
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
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(tab));
            }
        }

        static void DrawFilesView() => FilesView.Draw(Body, !Interaction.FilesDividerDragging);

        static void DrawGitView() => GitView.Draw(Body, !Interaction.FilesDividerDragging);

        static Rect ToRect(UiLayoutRect rect) => new Rect(rect.X, rect.Y, rect.Width, rect.Height);

        static void ClickFilesView()
        {
            if (!Interaction.FilesDividerInput && !Interaction.FilesDividerDragging)
                FilesView.Clicks();
        }

        static void ClickGitView()
        {
            if (!Interaction.FilesDividerInput && !Interaction.FilesDividerDragging)
                GitView.Clicks();
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
            Tab(
                r,
                folded ? TexButton.Reveal : TexButton.Collapse,
                folded,
                folded ? "Unfold all." : "Fold all.",
                () => SetAllFolds(!folded)
            );
            DrawAgentVisibilityAction(context.ShiftX(-(TabIcon + TabGap)));
        }

        static void DrawAgentVisibilityAction(SidebarTabActionContext context)
        {
            var r = ActionRect(context);
            _agentVisibilityRect = UiSelector.ScreenBox(TabHitRect(r));
            Tab(
                r,
                Icons.Filter,
                StatusFiltering,
                StatusFiltering
                    ? $"Showing {StatusFilterLabel} agents. Click to change the status filter."
                    : "All agents shown. Click to choose agent statuses.",
                OpenAgentVisibilityMenu,
                filter: true
            );
        }

        static void DrawFilesActions(SidebarTabActionContext context)
        {
            var r = ActionRect(context);
            DrawFoldAction(ref r);
            DrawVisibilityAction(r);
        }

        static void DrawSearchActions(SidebarTabActionContext context)
        {
            var r = ActionRect(context);
            DrawFoldAction(ref r);
        }

        static void DrawVisibilityAction(Rect r)
        {
            bool active = !Settings.SidebarShowHidden || !Settings.SidebarShowGitignored;
            _visibilityRect = UiSelector.ScreenBox(TabHitRect(r));
            Tab(
                r,
                Icons.Filter,
                active,
                active
                    ? "Dotfiles or Git-ignored files are hidden. Click to change the filters."
                    : "All files shown. Click to choose which files to show.",
                OpenVisibilityMenu,
                filter: true
            );
        }

        static void DrawGitActions(SidebarTabActionContext context)
        {
            var r = ActionRect(context);
            DrawFoldAction(ref r);
            Tab(
                r,
                Icons.Refresh,
                false,
                "Refresh Git status for every working tree.",
                GitView.Refresh,
                onPress: false
            );
        }

        static void RefreshLibrary() => LibraryView.Refresh(UiLayout.Fail);

        static void DrawLibraryActions(SidebarTabActionContext context)
        {
            var r = ActionRect(context);
            DrawFoldAction(ref r);
            DrawLibraryProjectFilterAction(ref r);
            Tab(
                r,
                Icons.Refresh,
                false,
                "Refresh the Library data.",
                RefreshLibrary,
                onPress: false
            );
        }

        static void DrawLibraryProjectFilterAction(ref Rect r)
        {
            var anchor = UiSelector.ScreenBox(TabHitRect(r));
            Tab(
                r,
                Icons.Filter,
                LibraryView.ProjectFiltering,
                LibraryView.ProjectFiltering
                    ? $"Showing {LibraryView.ProjectFilterLabel} in Library. Click to change the project filter."
                    : "Library shows every project. Click to filter the list.",
                () => LibraryView.OpenProjectFilterMenu(anchor),
                filter: true
            );
            r.x -= TabIcon + TabGap;
        }

        static void DrawTasksActions(SidebarTabActionContext context)
        {
            var r = ActionRect(context);
            Tab(
                r,
                Icons.Refresh,
                false,
                "Refresh the task list.",
                () => SessionHub.Instance.TaskStore.Refresh(fail: UiLayout.Fail),
                onPress: false
            );
            r.x -= TabIcon + TabGap;
            var anchor = UiSelector.ScreenBox(TabHitRect(r));
            Tab(
                r,
                Icons.Filter,
                TasksView.Filtering,
                TasksView.Filtering
                    ? "Task filters are active. Click to change the filters."
                    : "Showing all tasks. Click to filter by status, direction, or agent.",
                () => TasksView.OpenFilterMenu(anchor),
                filter: true
            );
        }

        static void DrawFoldAction(ref Rect r)
        {
            bool folded = AllFolded();
            Tab(
                r,
                folded ? TexButton.Reveal : TexButton.Collapse,
                folded,
                folded ? "Unfold all." : "Fold all.",
                () => SetAllFolds(!folded)
            );
            r.x -= TabIcon + TabGap;
        }
    }
}
