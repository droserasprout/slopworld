using System;

namespace SlopWorld
{
    // Session metadata and reader-command policy stay outside shared row drawing.
    public static class SessionRowAction
    {
        // Explicit reader intent survives daemon adoption through tmux metadata.
        // Configured commands can also identify a reader action.
        public static RowAct Of(SessionInfo info)
        {
            switch (info?.Intent)
            {
                case "view": case "search": return RowAct.View;
                case "edit": return RowAct.Edit;
                case "diff": return RowAct.Diff;
            }
            string cmd = (info?.Cmd ?? "").TrimStart();
            if (cmd.Length == 0) cmd = (info?.Agent ?? "").TrimStart();
            if (cmd.Length == 0) return RowAct.None;

            if (Pager.IsPagerCommand(cmd))
                return RowAct.View;
            if (Pager.IsEditorCommand(cmd)) return RowAct.Edit;
            // The git view's diff, which is a whole `git -C ... --paginate diff` line and the
            // only git this half ever runs in an errand.
            if (cmd.StartsWith("git ", StringComparison.Ordinal) && cmd.Contains(" diff")) return RowAct.Diff;
            return RowAct.None;
        }

    }
}
