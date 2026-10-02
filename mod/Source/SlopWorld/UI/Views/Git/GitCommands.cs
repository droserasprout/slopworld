namespace SlopWorld
{
    // Commands resolve HEAD when executed, so the first commit between refresh and click
    // cannot leave the view using an obsolete unborn-repository decision.
    internal static class GitCommands
    {
        public static string Diff(string root, string relative, bool untracked)
        {
            string git = "git -C " + PagerCommands.Quote(root);
            string pager = git + " -c " + PagerCommands.Quote("core.pager=LESS=R delta --paging=always") + " --paginate";
            if (untracked)
                return pager + " diff --color=always --no-index -- /dev/null " + PagerCommands.Quote(relative);
            string paths = relative == null ? "" : " -- " + PagerCommands.Quote(relative);
            // A shell alias runs at the repository root and inherits Git's -c options.
            // Keep the outer command a git invocation so PagerCommands can replace delta
            // appearance overrides on both initial launch and recovered reader sessions.
            // Materialize the empty tree using this repository's SHA-1 or SHA-256 format.
            string script = "!f() { if git rev-parse --verify HEAD >/dev/null 2>&1; then base=HEAD; " +
                "else base=$(git hash-object -w -t tree --stdin </dev/null) || return; fi; " +
                "git --paginate diff --color=always \"$base\" \"$@\"; }; f";
            return git + " -c " + PagerCommands.Quote("core.pager=LESS=R delta --paging=always") +
                " -c " + PagerCommands.Quote("alias.slopworld-diff=" + script) + " slopworld-diff" + paths;

        }

        public static string Unstage(string root, string relative)
        {
            string git = "git -C " + PagerCommands.Quote(root);
            string paths = " -- " + PagerCommands.Quote(relative);
            // With no commit all index entries are additions. Remove only index entries;
            // -f also permits unstaging a file edited again after it was added.
            string script = "if " + git + " rev-parse --verify HEAD >/dev/null 2>&1; then exec " +
                git + " reset" + paths + "; else exec " + git + " rm --cached -r -f" + paths + "; fi";
            return "bash -lc " + PagerCommands.Quote(script);
        }
    }
}
