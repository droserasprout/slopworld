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
                    "Agents - every session, under the project it runs in",
                    true, true, false,
                    new SidebarTabHandlers
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
                    "Files - every project's directory, as a tree",
                    true, true, true,
                    new SidebarTabHandlers
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
                            FilesView.ClearFocus();
                        },
                        Entered = () =>
                        {
                            FilesView.Entered();
                            GitView.Entered();
                        },
                        Reselected = FilesView.Entered,
                    }),
                new SidebarTabDefinition(
                    SidebarTab.Search, "search", "search",
                    "Search - find text across every project",
                    true, false, true,
                    new SidebarTabHandlers
                    {
                        Draw = () => SearchView.Draw(Body),
                        Click = SearchView.Clicks,
                        DrawActions = DrawSearchActions,
                        Refresh = SearchView.Search,
                        FilterChanged = SearchView.Search,
                        Close = SearchView.Closed,
                        Entered = SearchView.Entered,
                    }),
                new SidebarTabDefinition(
                    SidebarTab.Git, "git", "git",
                    "Git - what every working tree has that its last commit does not",
                    true, true, false,
                    new SidebarTabHandlers
                    {
                        Draw = DrawGitView,
                        Click = ClickGitView,
                        DrawActions = DrawGitActions,
                        Refresh = GitView.Refresh,
                        FilterChanged = GitView.Refresh,
                        SetAllFolds = GitView.SetAllFolded,
                        AllFolded = () => GitView.AllFolded,
                        Entered = GitView.Refresh,
                        Reselected = GitView.Refresh,
                    }),
                new SidebarTabDefinition(
                    SidebarTab.Tasks, "tasks", "tasks",
                    "Tasks - delegate work and inspect the agent mailbox",
                    true, false, false,
                    new SidebarTabHandlers
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
                    "Library - one-shot errands you can run against any project",
                    true, true, false,
                    new SidebarTabHandlers
                    {
                        Draw = () => LibraryView.Draw(Body),
                        Click = LibraryView.Clicks,
                        DrawActions = DrawLibraryActions,
                        Refresh = () => SessionHub.Instance.Catalog.RefreshLibrary(UiLayout.Fail),
                        // Fetch on entry as well, including when socket updates are unavailable.
                        Entered = () => SessionHub.Instance.Catalog.RefreshLibrary(),
                        SetAllFolds = LibraryView.SetAllFolded,
                        AllFolded = () => LibraryView.AllFolded,
                    }));
        }

        static void DrawFilesView()
        {
            var body = Body;
            PrepareRouted(SidebarTab.Files);
            var split = FilesSplit(body);
            HandleFilesDivider(body, split);
            split = FilesSplit(body);
            DrawRouted(ToRect(split.Upper), SidebarTab.Files, Interaction.FilesRoutedScroll);
            FilesView.Draw(ToRect(split.Lower), !Interaction.FilesDividerDragging);
            DrawFilesDivider(split, body);
        }

        static void DrawGitView()
        {
            var body = Body;
            PrepareRouted(SidebarTab.Git);
            float height = DrawRouted(body, SidebarTab.Git);
            GitView.Draw(TreeBody(body, height));
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
            if (!ClickRouted()) GitView.Clicks();
        }

        static void SetAgentFolds(bool folded)
        {
            foreach (var key in Layout.Order) Fold(key, folded);
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
                    ? $"Showing {StatusFilterLabel} agents. Click to change the selection."
                    : "All agents shown. Click to select statuses.",
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
                    ? "Visibility filters active. Click to change."
                    : "All files shown. Click to filter.",
                OpenVisibilityMenu);
        }

        static void DrawGitActions(SidebarTabActionContext context)
        {
            var r = ActionRect(context);
            DrawFoldAction(ref r);
            Tab(r, Icons.Refresh, false,
                "Read every working tree again.", GitView.Refresh);
        }

        static void DrawLibraryActions(SidebarTabActionContext context)
        {
            var r = ActionRect(context);
            DrawFoldAction(ref r);
        }

        static void DrawTasksActions(SidebarTabActionContext context)
        {
            var r = ActionRect(context);
            Tab(r, Icons.Refresh, false,
                "Read the task mailbox again.",
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
