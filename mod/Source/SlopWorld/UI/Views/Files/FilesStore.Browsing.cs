using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Bounds listing concurrency and rejects stale replies before merging directory snapshots.
    sealed partial class FilesStore
    {
        const int MaxConcurrentBrowse = 4;
        readonly Queue<BrowseRequest> _browseQueue = new Queue<BrowseRequest>();
        int _browseInFlight;

        sealed class BrowseRequest
        {
            public FileNode Node;
            public string Path;
            public int Version;
            public bool Descend;
        }


        void Fetch(FileNode node, System.Action done = null)
        {
            // Join an active refresh before consulting its stale children.
            if (!node.Loading && node.Children != null && node.Error == null) { done?.Invoke(); return; }
            if (done != null)
            {
                if (node.Loaded == null) node.Loaded = new List<System.Action>();
                node.Loaded.Add(done);
            }
            if (node.Loading) return;
            node.Loading = true;
            node.Error = null;
            BumpTree();
            QueueBrowse(node, false);
        }

        void QueueBrowse(FileNode node, bool descend)
        {
            node.ListingVersion++;
            _browseQueue.Enqueue(new BrowseRequest
            {
                Node = node,
                Path = node.Path,
                Version = node.ListingVersion,
                Descend = descend,
            });
            PumpBrowse();
        }

        void PumpBrowse()
        {
            while (_browseInFlight < MaxConcurrentBrowse && _browseQueue.Count > 0)
            {
                var request = _browseQueue.Dequeue();
                if (!Current(request)) continue;

                _browseInFlight++;
                DaemonClient.Get<Wire.BrowseResult>(
                    BrowseUrl(request.Path),
                    j => CompleteBrowse(request, j, null),
                    msg => CompleteBrowse(request, null, msg));
            }
        }

        static string BrowseUrl(string path) => WireProtocol.Routes.Browse +
            "?files=1&path=" + Uri.EscapeDataString(path) +
            "&hidden=" + (Settings.SidebarShowHidden ? "1" : "0") +
            "&gitignore=" + (Settings.SidebarShowGitignored ? "0" : "1");

        bool Current(BrowseRequest request) =>
            (request.Node.Project == null || SidebarScopes.Enabled(request.Node.Project)) &&
            request.Node.Loading && request.Node.Path == request.Path &&
            request.Node.ListingVersion == request.Version;

        void CompleteBrowse(BrowseRequest request, Wire.BrowseResult j, string error)
        {
            _browseInFlight--;
            try
            {
                if (!Current(request)) return;

                var node = request.Node;
                node.Loading = false;
                if (j == null)
                {
                    node.Error = error;
                    node.Loaded = null;
                    BumpTree();
                    return;
                }

                node.Children = Listed(node, j);
                node.More = j.Truncated;
                node.HasChildren = node.Children.Count > 0 || node.More;
                BumpTree();
                var loaded = node.Loaded;
                node.Loaded = null;
                if (loaded != null)
                    foreach (var callback in loaded) callback();

                if (!request.Descend) return;
                // Walk the fresh children, rather than the old list. Refresh loaded collapsed
                // children so their visible rows keep accurate disclosure state, but only
                // descend through branches that remain expanded.
                foreach (var child in node.Children)
                    if (child.IsDir && child.Children != null)
                        RefreshLoaded(child, child.Expanded);
            }
            finally
            {
                PumpBrowse();
            }
        }

        static List<FileNode> Listed(FileNode parent, Wire.BrowseResult j)
        {
            var previous = new Dictionary<string, FileNode>();
            if (parent.Children != null)
                foreach (var child in parent.Children)
                    previous[child.Name] = child;

            var children = new List<FileNode>();
            var empty = new HashSet<string>(j.EmptyDirs,
                StringComparer.Ordinal);
            var ignoredDirs = new HashSet<string>(
                j.GitignoredDirs, StringComparer.Ordinal);
            var ignoredFiles = new HashSet<string>(
                j.GitignoredFiles, StringComparer.Ordinal);
            foreach (var d in j.Dirs)
            {
                string name = d;
                children.Add(ReuseOrChild(parent, name, true, previous, !empty.Contains(name),
                    ignoredDirs.Contains(name)));
            }
            foreach (var f in j.Files)
            {
                string name = f;
                children.Add(ReuseOrChild(parent, name, false, previous, false,
                    ignoredFiles.Contains(name)));
            }
            return children;
        }

        static FileNode ReuseOrChild(FileNode parent, string name, bool dir, Dictionary<string, FileNode> previous,
            bool hasChildren, bool gitignored)
        {
            if (previous.TryGetValue(name, out var child) && child.IsDir == dir)
            {
                child.Path = parent.Path.TrimEnd('/') + "/" + name;
                child.Root = parent.Root;
                child.Project = parent.Project;
                child.Depth = parent.Depth + 1;
                // The parent's empty check ignores gitignore; prefer a cached filtered listing.
                child.HasChildren = dir && child.Children != null
                    ? child.Children.Count > 0 || child.More
                    : hasChildren;
                child.Gitignored = gitignored;
                if (dir && !hasChildren)
                {
                    // A parent refresh can learn that a previously expanded directory was
                    // emptied. Drop its old rows immediately instead of waiting for a click.
                    child.Children = new List<FileNode>();
                    child.More = false;
                    child.HasChildren = false;
                }
                return child;
            }
            return Child(parent, name, dir, hasChildren, gitignored);
        }

        static FileNode Child(FileNode parent, string name, bool dir, bool hasChildren, bool gitignored) => new FileNode
        {
            Name = name,
            Path = parent.Path.TrimEnd('/') + "/" + name,
            IsDir = dir,
            Root = parent.Root,
            Project = parent.Project,
            Depth = parent.Depth + 1,
            HasChildren = hasChildren,
            Gitignored = gitignored,
        };
    }
}
