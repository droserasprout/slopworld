using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static partial class Sidebar
    {
        static readonly SidebarTabRegistry TabRegistry = new SidebarTabRegistry(ConfigureTab);

        static SidebarTabDefinition CurrentDefinition =>
            TabRegistry.FromPersisted(Settings.SidebarTab);

        static void ConfigureTab(SidebarTabDefinition definition)
        {
            switch (definition.Tab)
            {
                case SidebarTab.Agents:
                    definition.Draw = DrawAgentTab;
                    definition.Click = Menus;
                    definition.DrawActions = DrawAgentActions;
                    definition.Refresh = () => SessionHub.Instance.SessionStore.Refresh();
                    definition.SetAllFolds = SetAgentFolds;
                    definition.AllFolded = AllAgentsFolded;
                    break;
                case SidebarTab.Files:
                    definition.Draw = DrawFilesView;
                    definition.Click = ClickFilesView;
                    definition.DrawActions = DrawFilesActions;
                    definition.Refresh = FilesView.Reload;
                    definition.SetAllFolds = FilesView.SetAllFolded;
                    definition.AllFolded = () => FilesView.AllFolded;
                    definition.Close = () =>
                    {
                        EndFilesDivider();
                        GitView.CancelPendingDiff();
                        FilesView.ClearFocus();
                    };
                    definition.Entered = () =>
                    {
                        FilesView.Entered();
                        GitView.Entered();
                    };
                    definition.Reselected = () =>
                    {
                        FilesView.Entered();
                        GitView.Refresh();
                    };
                    break;
                case SidebarTab.Git:
                    definition.Draw = DrawGitView;
                    definition.Click = ClickGitView;
                    definition.DrawActions = DrawGitActions;
                    definition.Refresh = GitView.Refresh;
                    definition.FilterChanged = GitView.Refresh;
                    definition.SetAllFolds = GitView.SetAllFolded;
                    definition.AllFolded = () => GitView.AllFolded;
                    definition.Close = () =>
                    {
                        EndFilesDivider();
                        GitView.CancelPendingDiff();
                    };
                    definition.Entered = GitView.Refresh;
                    definition.Reselected = GitView.Refresh;
                    break;
                case SidebarTab.Search:
                    definition.Draw = () => SearchView.Draw(Body);
                    definition.Click = SearchView.Clicks;
                    definition.DrawActions = DrawSearchActions;
                    definition.Refresh = SearchView.Search;
                    definition.SetAllFolds = SearchView.SetAllFolded;
                    definition.AllFolded = () => SearchView.AllFolded;
                    definition.FilterChanged = SidebarScopes.Update;
                    definition.Close = SearchView.Closed;
                    definition.Entered = SearchView.Entered;
                    break;
                case SidebarTab.Tasks:
                    definition.Draw = () => TasksView.Draw(Body);
                    definition.Click = TasksView.Clicks;
                    definition.DrawActions = DrawTasksActions;
                    definition.Refresh = () =>
                        SessionHub.Instance.TaskStore.Refresh(fail: UiLayout.Fail);
                    definition.Entered = () =>
                        SessionHub.Instance.TaskStore.Refresh(fail: UiLayout.Fail);
                    definition.Reselected = () =>
                        SessionHub.Instance.TaskStore.Refresh(fail: UiLayout.Fail);
                    break;
                case SidebarTab.Library:
                    definition.Draw = () => LibraryView.Draw(Body);
                    definition.Click = LibraryView.Clicks;
                    definition.Close = LibraryView.Closed;
                    definition.DrawActions = DrawLibraryActions;
                    definition.Refresh = RefreshLibrary;
                    definition.Reselected = RefreshLibrary;
                    // Fetch on entry as well, including when socket updates are unavailable.
                    definition.Entered = () => LibraryView.Refresh();
                    definition.SetAllFolds = LibraryView.SetAllFolded;
                    definition.AllFolded = () => LibraryView.AllFolded;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(definition));
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
                    ? $"Showing {StatusFilterLabel}. Click to change the Agents filter."
                    : "All agents and shells shown. Click to filter the list.",
                OpenAgentVisibilityMenu,
                filter: true,
                resetFilter: () => SetStatusFilter(AgentStatusFilter.All)
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
                filter: true,
                resetFilter: ResetFileVisibility
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
                filter: true,
                resetFilter: LibraryView.ResetProjectFilter
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
                filter: true,
                resetFilter: TasksView.ResetFilters
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
