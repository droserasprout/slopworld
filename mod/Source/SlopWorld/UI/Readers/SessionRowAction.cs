using System;

namespace SlopWorld
{
    // Session metadata and reader-command policy stay outside shared row drawing.
    public static class SessionRowAction
    {
        // Classify session actions from `Cmd`/`Agent`: ephemeral commands may be in `Cmd`,
        // configured commands in `Agent`. After restart, adopted sessions lack metadata, so
        // view/edit/diff name prefixes are the fallback for routing them to Files/Git.
        public static RowAct Of(SessionInfo info)
        {
            switch (info?.Intent)
            {
                case "view": case "search": return RowAct.View;
                case "edit": return RowAct.Edit;
                case "diff": return RowAct.Diff;
            }
            // An adopted ephemeral session has no saved command, so the daemon resolves its
            // blank config through the default agent command. Its generated action prefix is
            // the authoritative identity in that case, and must win before that fallback.
            if (info?.Ephemeral == true)
            {
                RowAct named = ByName(info.Name);
                if (named != RowAct.None) return named;
            }

            string cmd = (info?.Cmd ?? "").TrimStart();
            if (cmd.Length == 0) cmd = (info?.Agent ?? "").TrimStart();
            if (cmd.Length == 0)
            {
                return ByName(info?.Name);
            }

            if (Pager.IsPagerCommand(cmd))
                return RowAct.View;
            if (Pager.IsEditorCommand(cmd)) return RowAct.Edit;
            // The git view's diff, which is a whole `git -C ... --paginate diff` line and the
            // only git this half ever runs in an errand.
            if (cmd.StartsWith("git ", StringComparison.Ordinal) && cmd.Contains(" diff")) return RowAct.Diff;
            return RowAct.None;
        }

        static RowAct ByName(string name)
        {
            name = (name ?? "").TrimStart();
            if (name.StartsWith("view-", System.StringComparison.Ordinal)
                || name.StartsWith("search-", System.StringComparison.Ordinal)
                || name.StartsWith("link-", System.StringComparison.Ordinal))
                return RowAct.View;
            if (name.StartsWith("edit-", System.StringComparison.Ordinal)) return RowAct.Edit;
            if (name.StartsWith("diff-", System.StringComparison.Ordinal)) return RowAct.Diff;
            return RowAct.None;
        }

    }
}
