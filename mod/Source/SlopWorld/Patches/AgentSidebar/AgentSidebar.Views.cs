using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // View selection, project/status filtering, and view-owned refreshes.
    public static partial class AgentSidebar
    {
        public static void FocusTerminal() => Show(SidebarTab.Agents);

        public static void ShowAgents() => Show(SidebarTab.Agents);

        // Content views use the same focus path as an agent-row click: expose the agent tab,
        // then put that agent's pane on show.
        public static void FocusAgent(string name)
        {
            if (string.IsNullOrEmpty(name) || name == TaskInfo.Host ||
                SessionHub.Instance.Get(name) == null) return;
            Show(SidebarTab.Agents);
            TerminalWindow.Open(name);
        }

        public static void ShowFiles() => Show(SidebarTab.Files);
        public static void ShowSearch() => Show(SidebarTab.Search);
        public static void ShowGit() => Show(SidebarTab.Git);
        public static void ShowLibrary() => Show(SidebarTab.Library);
        public static void ShowTasks() => Show(SidebarTab.Tasks);

        public static bool CanFoldCurrent => CurrentTab != SidebarTab.Search &&
            CurrentTab != SidebarTab.Tasks;
        public static bool CurrentViewAllFolded => AllFolded();
        public static bool CanToggleDotfiles => CurrentTab == SidebarTab.Files ||
            CurrentTab == SidebarTab.Search;

        public static void ToggleDotfiles()
        {
            if (!CanToggleDotfiles) return;
            Settings.S.sidebarShowHidden = !Settings.S.sidebarShowHidden;
            Settings.S.Write();
            ReloadAfterVisibilityChange();
        }

        public static void ToggleGitignored()
        {
            if (!CanToggleDotfiles) return;
            Settings.S.sidebarShowGitignored = !Settings.S.sidebarShowGitignored;
            Settings.S.Write();
            ReloadAfterVisibilityChange();
        }

        static void ReloadAfterVisibilityChange()
        {
            if (CurrentTab == SidebarTab.Files) FilesView.Reload();
            else SearchView.Search();
        }

        public static void SetAllFolds(bool folded)
        {
            if (!CanFoldCurrent) return;
            switch (CurrentTab)
            {
                case SidebarTab.Agents:
                    foreach (var key in Layout.Order) Fold(key, folded);
                    break;
                case SidebarTab.Files: FilesView.SetAllFolded(folded); break;
                case SidebarTab.Git: GitView.SetAllFolded(folded); break;
                case SidebarTab.Library: LibraryView.SetAllFolded(folded); break;
            }
        }

        public static void RefreshCurrentView()
        {
            switch (CurrentTab)
            {
                case SidebarTab.Agents: SessionHub.Instance.Refresh(); break;
                case SidebarTab.Files: FilesView.Reload(); break;
                case SidebarTab.Search: SearchView.Search(); break;
                case SidebarTab.Git: GitView.Refresh(); break;
                case SidebarTab.Library:
                    SessionHub.Instance.RefreshLibrary(SlopWidgets.Fail);
                    break;
                case SidebarTab.Tasks:
                    SessionHub.Instance.RefreshTasks(SlopWidgets.Fail);
                    break;
            }
        }

        // The visual rect fills the panel while its hit rect stops before Grip so one consumed click cannot resize and open the menu.


        public static SidebarTab CurrentTab => ParseTab(Settings.SidebarTab);

        static SidebarTab ParseTab(string value)
        {
            switch (value)
            {
                case "files": return SidebarTab.Files;
                case "search": return SidebarTab.Search;
                case "git": return SidebarTab.Git;
                case "library": return SidebarTab.Library;
                case "tasks": return SidebarTab.Tasks;
                default: return SidebarTab.Agents;
            }
        }

        static string TabName(SidebarTab tab)
        {
            switch (tab)
            {
                case SidebarTab.Files: return "files";
                case SidebarTab.Search: return "search";
                case SidebarTab.Git: return "git";
                case SidebarTab.Library: return "library";
                case SidebarTab.Tasks: return "tasks";
                default: return "agents";
            }
        }

        // Which views own a second row. Keep in step with what [Actions] draws.
        static bool HasActions => CurrentTab == SidebarTab.Agents
            || CurrentTab == SidebarTab.Files
            || CurrentTab == SidebarTab.Search
            || CurrentTab == SidebarTab.Git
            || CurrentTab == SidebarTab.Library
            || CurrentTab == SidebarTab.Tasks;

        // Empty means all projects. Unknown project keys show no rows while the daemon list is
        // incomplete; the no-project bucket is a normal filter key.
        public const string NoProject = SidebarProjectState.NoProject;

        public static bool Filtering => Projects.Filtering;

        const AgentStatusFilter EveryStatus = AgentStatusFilter.Active |
            AgentStatusFilter.Idle | AgentStatusFilter.Down;

        public static AgentStatusFilter StatusFilter => ParseStatusFilter(Settings.SidebarAgentStatus);

        public static bool StatusFiltering => StatusFilter != AgentStatusFilter.All;

        public static string StatusFilterLabel => StatusFilter.ToString();

        static AgentStatusFilter ParseStatusFilter(string value)
        {
            if (string.IsNullOrEmpty(value)) return AgentStatusFilter.All;

            var filter = AgentStatusFilter.All;
            var values = value.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in values)
            {
                switch (raw.Trim().ToLowerInvariant())
                {
                    case "active": filter |= AgentStatusFilter.Active; break;
                    case "idle": filter |= AgentStatusFilter.Idle; break;
                    case "down": filter |= AgentStatusFilter.Down; break;
                    case "all": return AgentStatusFilter.All;
                }
            }

            return filter == EveryStatus ? AgentStatusFilter.All : filter;
        }

        public static bool PassesStatus(AgentState state)
        {
            var filter = StatusFilter;
            if (filter == AgentStatusFilter.All) return true;

            switch (state)
            {
                case AgentState.Working:
                case AgentState.Waiting:
                    return (filter & AgentStatusFilter.Active) != 0;
                case AgentState.Idle:
                    return (filter & AgentStatusFilter.Idle) != 0;
                case AgentState.Down:
                    return (filter & AgentStatusFilter.Down) != 0;
                default:
                    return false;
            }
        }

        public static void SetStatusFilter(AgentStatusFilter filter)
        {
            if ((filter & EveryStatus) == EveryStatus) filter = AgentStatusFilter.All;

            var values = new List<string>();
            if ((filter & AgentStatusFilter.Active) != 0) values.Add("active");
            if ((filter & AgentStatusFilter.Idle) != 0) values.Add("idle");
            if ((filter & AgentStatusFilter.Down) != 0) values.Add("down");
            Settings.S.sidebarAgentStatus = values.Count == 0
                ? "all"
                : string.Join(",", values.ToArray());
            Settings.S.Write();
        }

        public static bool Ticked(string key) => Projects.Ticked(key);

        public static bool Passes(string project) => Projects.Passes(project);

        // What the filter is, for a tooltip or an empty line: the name when it is one name,
        // and a count when it is more.
        public static string FilterLabel
        {
            get
            {
                return Projects.FilterLabel;
            }
        }

        // A blank key clears the filter outright: "all projects" is no filter at all rather
        // than every name ticked, so a project made later is in it too.
        public static void ToggleFilter(string key)
        {
            Projects.ToggleFilter(key);

            // The agents, files and Library views read the filter as they draw. The other
            // two hold what they asked the daemon for, and a filter that widened is a
            // project they never asked about.
            if (CurrentTab == SidebarTab.Search) SearchView.Search();
            else if (CurrentTab == SidebarTab.Git) GitView.Refresh();
        }

        static void Show(SidebarTab tab)
        {
            // The tab is a new focus target even when it is already selected (Git refreshes
            // on that path), so a menu opened by the previous view must not survive it.
            SlopMenu.CloseAll();

            // Focusing Git is also the user's way to ask what changed since the last
            // focus, including when Git is already the selected tab.
            if (CurrentTab == tab)
            {
                if (tab == SidebarTab.Git) GitView.Refresh();
                else if (tab == SidebarTab.Tasks) SessionHub.Instance.RefreshTasks(SlopWidgets.Fail);
                return;
            }

            if (tab != SidebarTab.Files) FilesView.ClearFocus();
            if (tab != SidebarTab.Files) FilesView.ReleaseViewer();
            if (tab != SidebarTab.Search)
            {
                SearchView.Closed();
            }
            if (tab != SidebarTab.Git) GitView.ReleaseViewer();

            var s = Settings.S;
            s.sidebarTab = TabName(tab);
            s.Write();

            if (tab == SidebarTab.Git) GitView.Refresh();
            else if (tab == SidebarTab.Files) GitView.Entered();

            if (tab == SidebarTab.Search) SearchView.Entered();

            if (tab == SidebarTab.Library) SessionHub.Instance.RefreshLibrary();
            if (tab == SidebarTab.Tasks) SessionHub.Instance.RefreshTasks(SlopWidgets.Fail);
        }

        static RowAct RoutedAction(SessionInfo info) => RowActions.Of(info);

        public static bool IsRouted(SessionInfo info)
        {
            RowAct act = RoutedAction(info);
            return (act & (RowAct.View | RowAct.Edit | RowAct.Diff)) != 0;
        }

        static bool InTab(SessionInfo info, SidebarTab tab)
        {
            RowAct act = RoutedAction(info);
            return tab == SidebarTab.Files
                ? (act & (RowAct.View | RowAct.Edit)) != 0
                : tab == SidebarTab.Git && (act & RowAct.Diff) != 0;
        }

        static List<SessionInfo> RoutedFor(SidebarTab tab)
        {
            Layout.Routed.Clear();
            foreach (var info in SessionHub.Instance.Sessions)
            {
                if (!InTab(info, tab) || !Passes(info.Project)) continue;
                // Git owns one replaceable pager. Keep its old routed row until the new session
                // is handed to the terminal, so replacing a diff cannot resize the tree twice.
                if (tab == SidebarTab.Git && !GitView.IsViewerSession(info.Name)) continue;
                Layout.Routed.Add(info);
            }
            Layout.Routed.Sort(ByName);
            return Layout.Routed;
        }

        public static float RoutedHeight(SidebarTab tab) => RoutedFor(tab).Count * GhostH;

        public static Rect TreeBody(Rect body, SidebarTab tab) =>
            new Rect(body.x, body.y + RoutedHeight(tab), body.width,
                Mathf.Max(0f, body.height - RoutedHeight(tab)));

        public static List<string> Sessions()
        {
            var order = new List<string>();

            if (CurrentTab == SidebarTab.Agents)
            {
                foreach (var row in Layout.Rows)
                {
                    var info = row.Session == null ? null : SessionHub.Instance.Get(row.Session);
                    if (row.Session != null && !row.Ghost && !row.Worker && info != null &&
                        PassesStatus(info.State))
                        order.Add(row.Session);
                }
                return order;
            }

            // Tree views have no Rows, but Alt+number must still return to an agent.
            foreach (var key in Layout.Order)
                foreach (int i in Layout.Buckets[key])
                    if (Layout.Named.TryGetValue(i, out var session) && session != null)
                    {
                        var info = SessionHub.Instance.Get(session);
                        if (info != null && PassesStatus(info.State)) order.Add(session);
                    }
            return order;
        }

        public static List<string> WalkOrder()
        {
            var order = new List<string>();
            foreach (var row in Layout.Rows)
            {
                var info = row.Session == null ? null : SessionHub.Instance.Get(row.Session);
                if (row.Session != null && info != null && PassesStatus(info.State))
                    order.Add(row.Session);
            }
            return order;
        }

    }
}
