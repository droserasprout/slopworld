using System;
using System.IO;

namespace SlopWorld
{
    internal enum PanelStatusKind
    {
        View,
        Edit,
        Diff,
        Terminal,
    }

    // Content identity for the top bar. Reader metadata owns source locations; runtime
    // titles remain the agent summary and never supply a reader's file identity.
    internal sealed class PanelStatus
    {
        internal readonly string Title,
            Context,
            Tooltip;
        internal readonly bool MainCheckout,
            ShowMainCheckout;
        internal readonly PanelStatusKind Kind;

        PanelStatus(
            string title,
            string context,
            string tooltip,
            bool main,
            PanelStatusKind kind,
            bool showMainCheckout
        )
        {
            Title = title;
            Context = context;
            Tooltip = tooltip;
            MainCheckout = main;
            ShowMainCheckout = showMainCheckout;
            Kind = kind;
        }

        internal static string ScopeKey(SessionInfo info, ProjectInfo project) =>
            !string.IsNullOrEmpty(info.ReaderScope) ? info.ReaderScope
            : project != null
                ? BrowseScope.Identity(
                    project.Id,
                    string.IsNullOrEmpty(info.Worktree) ? "main" : info.Worktree
                )
            : info.Project;

        internal static PanelStatus From(
            SessionInfo info,
            RowAct action,
            ProjectInfo project,
            BrowseScope scope,
            bool hasDaemonSession = true,
            bool soleMainCheckout = false
        )
        {
            if (info == null)
                return null;
            PanelStatusKind kind;
            switch (action)
            {
                case RowAct.Edit:
                    kind = PanelStatusKind.Edit;
                    break;
                case RowAct.View:
                    kind = PanelStatusKind.View;
                    break;
                case RowAct.Diff:
                    kind = PanelStatusKind.Diff;
                    break;
                default:
                    if (!info.Host && !info.Ephemeral)
                        return null;
                    kind = PanelStatusKind.Terminal;
                    break;
            }

            // Captured reader scope wins over mutable session/project checkout metadata.
            string worktree = scope?.Worktree;
            if (string.IsNullOrEmpty(worktree))
                worktree = BrowseScope.WorktreeOf(info.ReaderScope);
            if (string.IsNullOrEmpty(worktree))
                worktree = string.IsNullOrEmpty(info.Worktree) ? "main" : info.Worktree;
            string projectName = project?.Name ?? scope?.Project ?? info.Project;
            bool main = !string.IsNullOrEmpty(projectName) && worktree == "main";
            bool showMainCheckout = main && !soleMainCheckout;
            string checkout = main ? "" : scope?.Name;
            if (!main && string.IsNullOrEmpty(checkout) && worktree == info.Worktree)
                checkout = info.WorktreeName;
            if (!main && string.IsNullOrEmpty(checkout) && !string.IsNullOrEmpty(projectName))
                checkout = worktree;
            string path = info.ReaderPath;
            string title;
            if (kind == PanelStatusKind.Diff)
            {
                // Git's diff:<relative target> key is a display target, not a file path.
                // Empty keys describe the whole checkout. Do not parse labels or commands.
                string target =
                    info.ReaderKey?.StartsWith("diff:", StringComparison.Ordinal) == true
                        ? info.ReaderKey.Substring(5).TrimEnd('/')
                        : null;
                title =
                    string.IsNullOrEmpty(target) || target == "."
                        ? "Working changes"
                        : Path.GetFileName(target);
                if (string.IsNullOrEmpty(path))
                    path = target;
            }
            else if (!string.IsNullOrEmpty(path))
            {
                title = Path.GetFileName(path);
            }
            else
            {
                title =
                    kind == PanelStatusKind.Edit ? "Editor"
                    : kind == PanelStatusKind.View ? "File viewer"
                    : !string.IsNullOrEmpty(info.Label) ? info.Label
                    : "Terminal";
                if (kind == PanelStatusKind.Terminal)
                    path = info.Dir;
            }
            string context = checkout ?? "";
            if (kind == PanelStatusKind.View || kind == PanelStatusKind.Edit)
            {
                // Resolve against the captured checkout, never another checkout's root.
                string root = scope?.Path;
                if (string.IsNullOrEmpty(root) && main)
                    root = project?.ExpandedDir;
                string relative = PagerCommands.RelativeFilePath(root, path);
                context =
                    showMainCheckout && !string.IsNullOrEmpty(relative)
                        ? " · " + relative
                        : Join(checkout, relative);
            }
            string sessionId = hasDaemonSession ? info.Name : null;
            string tooltip = JoinLines(
                title,
                path,
                Join(projectName, main ? "Main worktree" : checkout),
                string.IsNullOrEmpty(sessionId) ? null : "Session: " + sessionId
            );
            return new PanelStatus(title, context, tooltip, main, kind, showMainCheckout);
        }

        static string Join(params string[] parts) =>
            string.Join(" · ", Array.FindAll(parts, p => !string.IsNullOrEmpty(p)));

        static string JoinLines(params string[] parts) =>
            string.Join("\n", Array.FindAll(parts, p => !string.IsNullOrEmpty(p)));
    }
}
