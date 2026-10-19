using System;

namespace SlopWorld
{
    // Captures a reader's source location independently of its opaque process/header name.
    // ReaderScope is authoritative; legacy metadata resolves its project and worktree together.
    sealed class ReaderFileTarget
    {
        public readonly string Scope;
        public readonly string Path;
        public readonly int Line;

        ReaderFileTarget(string scope, string path, int line)
        {
            Scope = scope;
            Path = path;
            Line = line;
        }

        public static ReaderFileTarget From(SessionInfo info, ProjectInfo project)
        {
            // Command readers (including repository-wide diffs) need not name a file.
            // Never interpret ReaderKey or the session label as a filesystem path.
            if (string.IsNullOrEmpty(info?.ReaderPath))
                return null;
            string scope = info.ReaderScope;
            if (string.IsNullOrEmpty(scope))
                scope =
                    project == null
                        ? info.Project
                        : BrowseScope.Identity(
                            BrowseScope.ProjectIdOf(project),
                            string.IsNullOrEmpty(info.Worktree) ? "main" : info.Worktree
                        );
            return new ReaderFileTarget(
                scope,
                info.ReaderPath,
                (int)Math.Min(info.ReaderLine, (uint)int.MaxValue)
            );
        }
    }
}
