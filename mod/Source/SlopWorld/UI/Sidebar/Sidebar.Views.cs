using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // View selection, project/status filtering, and view-owned refreshes.
    public static partial class Sidebar
    {
        static readonly SidebarViewHistory ViewHistory = new SidebarViewHistory();
        static bool _restoringView;

        public static bool CanViewBack => ViewHistory.CanBack;
        public static bool CanViewForward => ViewHistory.CanForward;

        public static void FocusTerminal() => Show(SidebarTab.Agents);

        public static void ShowAgents() => Show(SidebarTab.Agents);

        // Content views use the same focus path as an agent-row click: expose the agent tab,
        // then put that agent's pane on show.
        public static void FocusAgent(string name)
        {
            if (
                string.IsNullOrEmpty(name)
                || name == TaskInfo.Host
                || SessionHub.Instance.Get(name) == null
            )
                return;
            ShowWithoutHistory(SidebarTab.Agents);
            RememberAgent(name);
            WorkspaceWindow.Open(name);
        }

        public static void ShowFiles() => Show(SidebarTab.Files);

        public static void ShowSearch() => Show(SidebarTab.Search);

        public static void ShowGit() => Show(SidebarTab.Git);

        public static void ShowLibrary() => Show(SidebarTab.Library);

        public static void ShowTasks() => Show(SidebarTab.Tasks);

        // The sidebar owns all view-specific routed readers. Keep terminal teardown in the
        // owner so a panel does not need to know which views retain session-backed previews.
        internal static void TerminalClosed(string session)
        {
            // Preserve the existing teardown order: Files, Search, then Git.
            FilesView.CloseViewerIf(session);
            SearchView.CloseViewerIf(session);
            GitView.CloseViewerIf(session);
        }

        internal static void ShowTab(SidebarTab tab) => Show(tab);

        // Ctrl+F1..F6 navigates to the most recent semantic target in that tab. A missing or
        // stale target is harmless: the requested tab remains visible and its normal refresh
        // path still runs.
        public static bool FocusLast(SidebarTab tab)
        {
            if (!ViewHistory.TryLast(tab, out var location))
            {
                Show(tab);
                return false;
            }

            // Visit the target itself, rather than first inserting a tab-only point. The
            // previous visible location should be the first result of View: Back.
            ViewHistory.Visit(location);
            return Restore(location);
        }

        public static bool ViewBack()
        {
            if (!ViewHistory.Back(out var location))
                return false;
            Restore(location);
            return true;
        }

        public static bool ViewForward()
        {
            if (!ViewHistory.Forward(out var location))
                return false;
            Restore(location);
            return true;
        }

        // SessionSelectable is the central source for agent selection, including map clicks,
        // terminal opens and tab cycling. Keep only durable agent rows here. Routed viewers,
        // workers, host shells and one-shot errands have their own view location kinds.
        internal static void RememberAgent(string name)
        {
            if (_restoringView || string.IsNullOrEmpty(name))
                return;
            var info = SessionHub.Instance.Get(name);
            if (info == null || info.Host || info.Worker || info.Ephemeral || IsRouted(info))
                return;
            ViewHistory.Visit(SidebarViewLocation.Agent(name));
        }

        internal static void RememberFile(string project, string path)
        {
            if (_restoringView || CurrentTab != SidebarTab.Files || string.IsNullOrEmpty(path))
                return;
            ViewHistory.Visit(
                SidebarViewLocation.File(
                    SidebarScopes.Key(project),
                    SidebarScopes.Relative(project, path)
                )
            );
        }

        internal static void RememberSearch(string project, string path, int line)
        {
            if (_restoringView || string.IsNullOrEmpty(path))
                return;
            ViewHistory.Visit(SidebarViewLocation.Search(project, path, line));
        }

        internal static void RememberGit(string project, string path)
        {
            if (_restoringView || CurrentTab != SidebarTab.Git || string.IsNullOrEmpty(path))
                return;
            ViewHistory.Visit(SidebarViewLocation.Git(SidebarScopes.Key(project), path));
        }

        internal static void RememberTask(string id)
        {
            if (_restoringView || string.IsNullOrEmpty(id))
                return;
            ViewHistory.Visit(SidebarViewLocation.Task(id));
        }

        internal static void RememberLibrary(string name, bool template = false)
        {
            if (_restoringView || string.IsNullOrEmpty(name))
                return;
            ViewHistory.Visit(SidebarViewLocation.Library(name, template));
        }

        public static bool CanFoldCurrent => CurrentDefinition.CanFold;
        public static bool CurrentViewAllFolded => CurrentDefinition.AllFolded();
        public static bool CanToggleDotfiles => CurrentDefinition.CanToggleDotfiles;

        public static void ToggleDotfiles()
        {
            if (!CanToggleDotfiles)
                return;
            Settings.S.sidebarShowHidden = !Settings.S.sidebarShowHidden;
            Settings.S.Write();
            ReloadAfterVisibilityChange();
        }

        public static void ToggleGitignored()
        {
            if (!CanToggleDotfiles)
                return;
            Settings.S.sidebarShowGitignored = !Settings.S.sidebarShowGitignored;
            Settings.S.Write();
            ReloadAfterVisibilityChange();
        }

        static void ReloadAfterVisibilityChange()
        {
            CurrentDefinition.Refresh();
        }

        public static void SetAllFolds(bool folded)
        {
            if (!CanFoldCurrent)
                return;
            CurrentDefinition.SetAllFolds(folded);
        }

        public static void RefreshCurrentView()
        {
            CurrentDefinition.Refresh();
        }

        // The visual rect fills the panel while its hit rect stops before Grip so one consumed click cannot resize and open the menu.

        public static SidebarTab CurrentTab => CurrentDefinition.Tab;

        // TaskStore is driven from Root.Update, not OnGUI. Poll only while the task list can
        // actually be seen. Tab entry/reselection still requests an immediate refresh.
        public static bool TasksVisible =>
            Verse.Current.ProgramState == ProgramState.Playing
            && ColonistBarStrip.BarShown
            && !Settings.SidebarHidden
            && !Cutscene.Playing
            && CurrentTab == SidebarTab.Tasks;

        // Whether the view draws controls in the always-reserved second row.
        static bool HasActions => CurrentDefinition.HasActions;

        // Empty means all projects. Unknown project keys show no rows while the daemon list is
        // incomplete. The no-project bucket is a normal filter key.
        public const string NoProject = SidebarProjectState.NoProject;

        public static bool Filtering => Projects.Filtering;

        static string _statusFilterSource;
        static AgentStatusFilter _statusFilter;
        static bool _statusFilterReady;

        public static AgentStatusFilter StatusFilter
        {
            get
            {
                string source = Settings.SidebarAgentStatus;
                if (!_statusFilterReady || _statusFilterSource != source)
                {
                    _statusFilterSource = source;
                    _statusFilter = AgentStatusSelection.Parse(source);
                    _statusFilterReady = true;
                }
                return _statusFilter;
            }
        }

        public static bool StatusFiltering => StatusFilter != AgentStatusFilter.All;

        public static string StatusFilterLabel => StatusFilter.ToString();

        public static bool PassesStatus(AgentState state)
        {
            var filter = StatusFilter;
            if (filter == AgentStatusFilter.All)
                return true;

            switch (state)
            {
                case AgentState.Working:
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
            Settings.S.sidebarAgentStatus = AgentStatusSelection.Format(filter);
            Settings.S.Write();
        }

        public static bool Ticked(string key) => Projects.Ticked(key);

        public static bool Passes(string project) => Projects.Passes(project);

        // What the filter is, for a tooltip or an empty line: the name when it is one name,
        // and a count when it is more.
        public static string FilterLabel
        {
            get { return Projects.FilterLabel; }
        }

        // A blank key toggles All between unrestricted visibility (including future projects)
        // and an explicit empty selection.
        public static void ToggleFilter(string key)
        {
            if (key.Length == 0)
                Projects.ToggleFilter(key);
            else
                Projects.ToggleVisibility(key, SessionHub.Instance.Projects.Select(p => p.Name));
            NotifyFilterChanged();
        }

        public static void IncludeProject(string project)
        {
            Projects.IncludeProject(project);
            NotifyFilterChanged();
        }

        static void NotifyFilterChanged()
        {
            SidebarScopes.Update();
            // Views holding daemon results must request projects added to the filter.
            CurrentDefinition.FilterChanged();
        }

        static void Show(SidebarTab tab)
        {
            ViewHistory.VisitTab(tab);
            ShowWithoutHistory(tab);
        }

        internal static void ShowWithoutHistory(SidebarTab tab)
        {
            var target = TabRegistry.For(tab);
            SidebarTabActivation.Activate(
                CurrentDefinition,
                target,
                TabRegistry.Definitions,
                UiMenu.CloseAll,
                () =>
                {
                    Settings.S.sidebarTab = target.PersistedName;
                    Settings.S.Write();
                }
            );
        }

        static bool Restore(SidebarViewLocation location)
        {
            ShowWithoutHistory(location.Tab);
            _restoringView = true;
            try
            {
                switch (location.Kind)
                {
                    case SidebarViewLocationKind.Tab:
                        return true;
                    case SidebarViewLocationKind.Agent:
                        if (SessionHub.Instance.Get(location.Primary) == null)
                            return false;
                        WorkspaceWindow.Open(location.Primary);
                        return true;
                    case SidebarViewLocationKind.File:
                        return FilesView.FocusLocation(location.Primary, location.Secondary);
                    case SidebarViewLocationKind.Search:
                        return SearchView.FocusLocation(
                            location.Primary,
                            location.Secondary,
                            location.Line
                        );
                    case SidebarViewLocationKind.Git:
                        return GitView.FocusLocation(location.Primary, location.Secondary);
                    case SidebarViewLocationKind.Task:
                        return TasksView.FocusLocation(location.Primary);
                    case SidebarViewLocationKind.Library:
                        return LibraryView.FocusLocation(
                            location.Primary,
                            location.Secondary == "template"
                        );
                    default:
                        return false;
                }
            }
            finally
            {
                _restoringView = false;
            }
        }

        static RowAct RoutedAction(SessionInfo info) => SessionRowAction.Of(info);

        public static bool IsRouted(SessionInfo info)
        {
            RowAct act = RoutedAction(info);
            return (act & (RowAct.View | RowAct.Edit | RowAct.Diff)) != 0;
        }

        // Open beside follows the live sidebar, not the daemon's complete session inventory.
        // In particular, an unpinned diff's process remains queryable until it exits but has no
        // routed row. A stopped agent likewise has a row for restart actions, not a usable pane.
        internal static bool IsOpenBesideCandidate(SessionInfo info)
        {
            if (
                info == null
                || !info.Alive
                || !Pager.SessionVisibility.Includes(info)
                || !SidebarScopes.SessionChosen(info)
            )
                return false;
            return RoutedAction(info) != RowAct.Diff || FileReaders.IsSession(info.Name);
        }

        static readonly RoutedSessionRows RoutedCache = new RoutedSessionRows();

        static readonly OpenFilesVisibility OpenFilesLifetime = new OpenFilesVisibility();

        static bool ReaderOpening =>
            FileReaders.Tabs.Find(tab => tab.Opening) != null || SearchView.ReaderOpening;

        static bool IsOpenFileSession(SessionInfo info) =>
            Pager.SessionVisibility.Includes(info)
            && IsRouted(info)
            && ((RoutedAction(info) & RowAct.Diff) == 0 || FileReaders.Tabs.IsSession(info.Name));

        static void UpdateOpenFilesVisibility()
        {
            var hub = SessionHub.Instance;
            // A disconnected snapshot must not turn a temporary absence into a saved preference.
            if (!hub.Online)
                return;
            FileReaders.Restore();
            bool hasFiles = FilesView.HasNativeReaders || hub.Sessions.Any(IsOpenFileSession);
            // Preview replacement may retire the last process before its successor arrives.
            if (OpenFilesLifetime.ShouldHide(hasFiles, ReaderOpening))
                SetOpenFilesVisible(false);
        }

        public static void WalkReader(int direction)
        {
            PrepareRouted();
            string current = FilesView.CurrentViewerHeader ?? WorkspaceWindow.CurrentName;
            string target = RoutedSessionRows.Adjacent(Layout.Routed, current, direction);
            if (target == null)
                return;
            RevealOpenFiles();
            SessionSelectable.Current = target;
            OpenRouted(target);
        }

        static void PrepareRouted()
        {
            FileReaders.Restore();
            Layout.ViewRows.Clear();
            RoutedCache.Ensure(
                Layout.Routed,
                SessionHub.Instance.Sessions,
                SessionHub.Instance.SessionsVersion,
                Projects.Revision + SidebarScopes.Revision,
                SessionHub.Instance.Config.EffectivePager,
                SessionHub.Instance.Config.Editor,
                info => IsOpenFileSession(info) && SidebarScopes.SessionChosen(info),
                FilesView.AddRoutedPreviews,
                GhostH
            );
            PerfTrace.Count("sidebar-routed-rows", Layout.Routed.Count);
        }

        static float RoutedHeight => Layout.Routed.Count * GhostH;

        public static Rect TreeBody(Rect body, float routedHeight) =>
            new Rect(
                body.x,
                body.y + routedHeight,
                body.width,
                Mathf.Max(0f, body.height - routedHeight)
            );

        public static List<string> Sessions()
        {
            var order = new List<string>();

            if (CurrentTab == SidebarTab.Agents)
            {
                foreach (var row in Layout.Rows)
                {
                    var info = row.Session == null ? null : SnapshotGet(row.Session);
                    if (
                        row.Session != null
                        && !row.Ghost
                        && !row.Worker
                        && info != null
                        && PassesStatus(info.State)
                    )
                        order.Add(row.Session);
                }
                return order;
            }

            // Tree views clear geometry. Read the colonist inventory independently so
            // Alt+number works even before the Agents tab has ever been laid out.
            var agents = new List<SessionInfo>();
            var bar = Find.ColonistBar;
            if (bar != null)
                foreach (var entry in bar.Entries)
                {
                    var info = SnapshotGet(Session(entry.pawn));
                    if (
                        info != null
                        && !info.Host
                        && !info.Ephemeral
                        && !info.Worker
                        && !IsRouted(info)
                        && SidebarScopes.SessionChosen(info)
                        && PassesStatus(info.State)
                    )
                        agents.Add(info);
                }
            return SidebarAgentRows.OrderAgents(agents, AgentColony.CompareNames);
        }

        public static List<string> WalkOrder()
        {
            var order = new List<string>();
            foreach (var row in Layout.Rows)
            {
                var info = row.Session == null ? null : SnapshotGet(row.Session);
                if (row.Session != null && info != null && PassesStatus(info.State))
                    order.Add(row.Session);
            }
            return order;
        }
    }
}
