using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Own repository identity, status/count snapshots and refresh lifetimes. The view owns
    // folding, selection and drawing; transport and time are supplied by its adapter.
    internal sealed class GitStore
    {
        readonly Dictionary<string, Repo> Repos = new Dictionary<string, Repo>();
        readonly BoundedWork Requests = new BoundedWork(4);
        readonly Func<string, string> _directory;
        readonly Func<string, bool> _enabled;
        readonly Func<float> _now;
        readonly Action<string, Action<Wire.GitResult>, Action<string>, int> _request;
        internal event Action Changed;

        internal GitStore(
            Func<string, string> directory,
            Func<string, bool> enabled,
            Func<float> now,
            Action<string, Action<Wire.GitResult>, Action<string>, int> request
        )
        {
            _directory = directory;
            _enabled = enabled;
            _now = now;
            _request = request;
        }

        bool IsCurrent(Repo repo) =>
            Repos.TryGetValue(repo.Project, out var current)
            && ReferenceEquals(current, repo)
            && repo.Dir == _directory(repo.Project);

        internal Repo Known(string project) =>
            project != null
            && Repos.TryGetValue(project, out var repo)
            && IsCurrent(repo)
            && repo.IsRepo
            && repo.Error == null
                ? repo
                : null;

        internal void Invalidate(Func<string, bool> exists)
        {
            foreach (var key in Repos.Keys.ToList())
            {
                var repo = Repos[key];
                repo.Operations.Invalidate();
                repo.Refreshes.Reset();
                repo.NextRefresh = 0f;
                if (!exists(key))
                    Repos.Remove(key);
            }
        }

        internal void RefreshIfDue(IEnumerable<string> scopes)
        {
            foreach (var key in scopes)
            {
                var repo = Get(key);
                if (!repo.Loading && _now() >= repo.NextRefresh)
                    Fetch(key);
            }
        }

        // A directory holds children. A file holds the daemon's row.
        internal sealed class Node : IContentTreeNode
        {
            public string Name;
            public string Rel; // relative to the repository root, which is what git takes back
            public string Project;
            public string Source;
            public bool IsDir;
            public List<Node> Kids;
            public Repo Owner;
            public GitStatus Status; // the porcelain pair, files only
            public int Added = -1; // -1 is "git counted none or was not asked to count"
            public int Deleted = -1;
            public int Depth;

            string IContentTreeNode.Name => Name;
            string IContentTreeNode.Key => Rel;
            string IContentTreeNode.ScopeKey => Project;
            bool IContentTreeNode.IsDirectory => IsDir;
            int IContentTreeNode.Depth => Depth;
            bool IContentTreeNode.CanExpand => IsDir;
            bool IContentTreeNode.Loading => false;
            string IContentTreeNode.Error => null;
            bool IContentTreeNode.More => false;
            IEnumerable<IContentTreeNode> IContentTreeNode.Children => Kids;
        }

        // One checkout snapshot, keyed by stable project/worktree scope identity.
        internal sealed class Repo
        {
            public string Project; // stable browsing scope key, not a project-name filter value
            public string Dir; // the project's directory, which is what was asked about
            public string Root; // the repository's, which may be above it
            public string Branch;
            public bool IsRepo = true;
            public readonly RefreshQueue Refreshes = new RefreshQueue();
            public bool Loading => Refreshes.Loading;
            public float NextRefresh;
            public readonly OperationGate Operations = new OperationGate();
            public bool CountsComplete;
            public string Error;
            public bool Asked; // whether an answer has ever landed
            public bool Truncated; // the daemon capped both the tree and its partial summary
            public int Changed,
                Added,
                Deleted;
            public Node Tree;

            // Porcelain status by relative path, for Files lookups without a tree walk per row.
            public readonly Dictionary<string, string> Sources = new Dictionary<string, string>();
            public Dictionary<string, string> Changes = new Dictionary<string, string>();
        }

        internal Repo Get(string project)
        {
            var dir = _directory(project);

            // A project whose directory moved is a different repository under the same
            // heading, and what was known about the old one is not about this one.
            if (Repos.TryGetValue(project, out var repo) && repo.Dir == dir)
                return repo;

            repo?.Operations.Invalidate();
            repo?.Refreshes.Reset();
            repo = new Repo { Project = project, Dir = dir };
            Repos[project] = repo;
            Changed?.Invoke();
            return repo;
        }

        internal void Fetch(string project, System.Action refreshed = null)
        {
            if (!_enabled(project))
                return;
            var repo = Get(project);
            if (string.IsNullOrEmpty(repo.Dir))
            {
                refreshed?.Invoke();
                return;
            }
            if (!repo.Refreshes.Request(refreshed))
                return;
            bool showLoading = repo.Tree == null;
            bool clearError = repo.Error != null;
            repo.Error = null;
            if (showLoading || clearError)
                Changed?.Invoke();

            string dir = repo.Dir;
            long generation = repo.Operations.Begin();
            GetGit(
                repo,
                generation,
                WireProtocol.Routes.Git + "?counts=false&path=" + System.Uri.EscapeDataString(dir),
                j =>
                {
                    if (!repo.Operations.IsCurrent(generation) || !IsCurrent(repo))
                        return;
                    repo.Asked = true;

                    if (!j.Repo)
                    {
                        bool changed =
                            repo.IsRepo
                            || repo.Tree != null
                            || repo.Changed != 0
                            || repo.Added != 0
                            || repo.Deleted != 0
                            || repo.Changes.Count != 0;
                        ClearStatus(repo);
                        if (changed)
                            Changed?.Invoke();
                        FinishFetch(project, repo);
                        return;
                    }

                    bool sameStatus = SameStatus(repo, j);
                    bool sameTree =
                        sameStatus
                        && repo.Root == j.Root
                        && repo.Changed == (int)j.Changed
                        && repo.Truncated == j.Truncated;
                    bool changedState = !sameTree || repo.Branch != j.Branch;
                    repo.IsRepo = true;
                    repo.Root = j.Root;
                    repo.Branch = j.Branch;
                    repo.Changed = (int)j.Changed;
                    repo.Truncated = j.Truncated;
                    if (!sameTree)
                    {
                        repo.Changes.Clear();
                        repo.Sources.Clear();
                        repo.Added = repo.Deleted = 0;
                        repo.CountsComplete = false;
                        repo.Tree = Fold(repo, j.Files);
                    }
                    if (changedState)
                        Changed?.Invoke();
                    if (!repo.Refreshes.Pending && !repo.Truncated && repo.Changed > 0)
                        FetchCounts(repo, generation);
                    FinishFetch(project, repo);
                },
                msg =>
                {
                    if (!repo.Operations.IsCurrent(generation) || !IsCurrent(repo))
                        return;
                    repo.Asked = true;
                    repo.Error = msg;
                    // Clear stale status too, so Files cannot offer diffs from a failed read.
                    ClearStatus(repo);
                    Changed?.Invoke();
                    FinishFetch(project, repo);
                }
            );
        }

        static void ClearStatus(Repo repo)
        {
            repo.IsRepo = false;
            repo.Tree = null;
            repo.Root = repo.Branch = null;
            repo.Changed = repo.Added = repo.Deleted = 0;
            repo.CountsComplete = repo.Truncated = false;
            repo.Changes.Clear();
            repo.Sources.Clear();
        }

        void GetGit(
            Repo repo,
            long generation,
            string url,
            System.Action<Wire.GitResult> success,
            System.Action<string> fail
        )
        {
            Requests.Add(
                () => repo.Operations.IsCurrent(generation) && _enabled(repo.Project),
                done =>
                    _request(
                        url,
                        result =>
                        {
                            try
                            {
                                success(result);
                            }
                            finally
                            {
                                done();
                            }
                        },
                        error =>
                        {
                            try
                            {
                                fail?.Invoke(error);
                            }
                            finally
                            {
                                done();
                            }
                        },
                        15_000
                    )
            );
        }

        // The status request intentionally omits line counts. Keep the existing tree when its
        // path/status snapshot is identical so a five-second poll does not erase visible stats
        // and rebuild them when the optional count request returns.
        static bool SameStatus(Repo repo, Wire.GitResult result)
        {
            if (!repo.IsRepo || repo.Tree == null)
                return false;

            int paths = 0;
            foreach (var file in result.Files)
            {
                if (string.IsNullOrEmpty(file.Path))
                    continue;
                paths++;
                if (
                    !repo.Changes.TryGetValue(file.Path, out var status)
                    || status != file.Status
                    || !repo.Sources.TryGetValue(file.Path, out var source)
                    || source != file.Source
                )
                    return false;
            }
            return paths == repo.Changes.Count;
        }

        void FinishFetch(string project, Repo repo)
        {
            repo.NextRefresh = _now() + 5f;
            if (!IsCurrent(repo))
                return;
            var completion = repo.Refreshes.Complete();
            if (completion.NeedsFollowUp)
            {
                Fetch(project);
                return;
            }
            foreach (var callback in completion.Callbacks)
                callback();
        }

        // Keep the usable status tree if counting fails. A newer refresh owns its own
        // generation, and expanding/collapsing rows while counts arrive must survive.
        void FetchCounts(Repo repo, long generation)
        {
            GetGit(
                repo,
                generation,
                WireProtocol.Routes.Git + "?path=" + System.Uri.EscapeDataString(repo.Dir),
                j =>
                {
                    if (
                        !repo.Operations.IsCurrent(generation)
                        || !IsCurrent(repo)
                        || !j.CountsComplete
                    )
                        return;
                    var files = new Dictionary<string, Wire.GitFile>();
                    foreach (var f in j.Files)
                    {
                        string path = f.Path;
                        if (
                            !repo.Changes.TryGetValue(path, out var status)
                            || status != f.Status
                            || !repo.Sources.TryGetValue(path, out var source)
                            || source != f.Source
                        )
                            return;
                        files[path] = f;
                    }
                    // The checkout may have changed between the two requests.
                    if (files.Count != repo.Changes.Count)
                        return;
                    bool changed = ApplyCounts(repo.Tree, files);
                    int added = (int)j.Added;
                    int deleted = (int)j.Deleted;
                    changed |=
                        !repo.CountsComplete || repo.Added != added || repo.Deleted != deleted;
                    repo.Added = added;
                    repo.Deleted = deleted;
                    repo.CountsComplete = true;
                    if (changed)
                        Changed?.Invoke();
                },
                null
            );
        }

        static bool ApplyCounts(Node node, Dictionary<string, Wire.GitFile> files)
        {
            if (node == null)
                return false;
            bool changed = false;
            if (node.Kids != null)
                foreach (var child in node.Kids)
                    changed |= ApplyCounts(child, files);
            if (!files.TryGetValue(node.Rel, out var f))
                return changed;
            int added = !f.HasAdded ? -1 : (int)f.Added;
            int deleted = !f.HasDeleted ? -1 : (int)f.Deleted;
            if (node.Added == added && node.Deleted == deleted)
                return changed;
            node.Added = added;
            node.Deleted = deleted;
            return true;
        }

        // A selected rename needs both sides, including sources outside a selected directory.
        internal static string[] DiffPaths(Repo repo, string relative)
        {
            // Null keeps the repository-wide diff unrestricted in GitCommands.
            if (relative == null)
                return null;
            var paths = new HashSet<string> { relative };
            foreach (var change in repo.Changes)
            {
                if (
                    change.Key != relative
                    && !change.Key.StartsWith(relative.TrimEnd('/') + "/", StringComparison.Ordinal)
                )
                    continue;
                var status = new GitStatus(change.Value);
                if (
                    (status.Staged == 'R' || status.Worktree == 'R')
                    && repo.Sources.TryGetValue(change.Key, out var source)
                    && !string.IsNullOrEmpty(source)
                )
                    paths.Add(source);
            }
            return paths.ToArray();
        }

        internal static string[] UnstagePaths(Node node)
        {
            var paths = new HashSet<string> { node.Rel };
            AddRenameSources(node, paths);
            return paths.ToArray();
        }

        static void AddRenameSources(Node node, HashSet<string> paths)
        {
            // Copies leave their source intact; only renames stage its removal.
            if (node.Status.Staged == 'R' && !string.IsNullOrEmpty(node.Source))
                paths.Add(node.Source);
            if (node.Kids != null)
                foreach (var child in node.Kids)
                    AddRenameSources(child, paths);
        }

        // Requires sorted paths so each directory's entries are contiguous.
        // Truncated responses still produce a valid partial tree.
        static Node Fold(Repo repo, IEnumerable<Wire.GitFile> files)
        {
            var root = new Node
            {
                IsDir = true,
                Depth = -1,
                Kids = new List<Node>(),
                Rel = "",
                Project = repo.Project,
                Owner = repo,
            };

            foreach (var f in files)
            {
                string rel = f.Path;
                if (string.IsNullOrEmpty(rel))
                    continue;
                repo.Changes[rel] = f.Status;
                repo.Sources[rel] = f.Source;

                var parts = rel.Split('/');
                var at = root;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    string seg = parts[i];
                    // The last kid is the one to grow: the list is sorted, so anything else
                    // under this directory has already been added.
                    var last = at.Kids.Count > 0 ? at.Kids[at.Kids.Count - 1] : null;
                    if (last == null || !last.IsDir || last.Name != seg)
                    {
                        last = new Node
                        {
                            Name = seg,
                            IsDir = true,
                            Kids = new List<Node>(),
                            Depth = at.Depth + 1,
                            Rel = at.Rel.Length == 0 ? seg : at.Rel + "/" + seg,
                            Project = repo.Project,
                            Owner = repo,
                        };
                        at.Kids.Add(last);
                    }
                    at = last;
                }

                at.Kids.Add(
                    new Node
                    {
                        Name = parts[parts.Length - 1],
                        Rel = rel,
                        IsDir = false,
                        Depth = at.Depth + 1,
                        Status = new GitStatus(f.Status),
                        Source = f.Source,
                        // Null where git counted nothing (binary or a capped response). Kept apart
                        // from zero, which is a real count and a different row.
                        Added = !f.HasAdded ? -1 : (int)f.Added,
                        Deleted = !f.HasDeleted ? -1 : (int)f.Deleted,
                        Project = repo.Project,
                        Owner = repo,
                    }
                );
            }

            // Assign depths after collapsing directory chains, which moves descendants up.
            Squash(root);
            Depths(root, -1);
            return root;
        }

        // A directory with exactly one child, itself a directory, becomes one row named for
        // both. Bottom-up, so a chain of any length collapses in one pass.
        static void Squash(Node dir)
        {
            foreach (var kid in dir.Kids)
                if (kid.IsDir)
                    Squash(kid);

            for (int i = 0; i < dir.Kids.Count; i++)
            {
                var kid = dir.Kids[i];
                while (kid.IsDir && kid.Kids.Count == 1 && kid.Kids[0].IsDir)
                {
                    var only = kid.Kids[0];
                    only.Name = kid.Name + "/" + only.Name;
                    kid = only;
                }
                dir.Kids[i] = kid;
            }
        }

        static void Depths(Node dir, int depth)
        {
            dir.Depth = depth;
            if (dir.Kids == null)
                return;
            foreach (var kid in dir.Kids)
                Depths(kid, depth + 1);
        }
    }
}
