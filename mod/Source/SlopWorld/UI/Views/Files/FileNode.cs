using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Directory identity and its retained listing. Null Children means never loaded;
    // reload keeps the old children so a fresh listing can reuse expanded branches.
    sealed class FileNode : IContentTreeNode
    {
        public string Path;
        public string Name;
        public bool IsDir;
        public bool Expanded;
        public bool Loading;
        public string Error;
        public bool More;          // the daemon's cap cut the listing short
        public List<FileNode> Children;
        public bool HasChildren;
        public bool Gitignored;
        public string Root;        // the project dir this hangs off, for a relative path
        public string Project;     // stable browsing scope key, resolved to project/worktree at the request boundary
        public int Depth;
        // Identifies each listing and invalidates delayed work when Reload forgets this node.
        public int ListingVersion;
        public List<System.Action> Loaded;

        string IContentTreeNode.Name => Name;
        string IContentTreeNode.Key => Relative ?? "";
        string IContentTreeNode.ScopeKey => Project;
        bool IContentTreeNode.IsDirectory => IsDir;
        int IContentTreeNode.Depth => Depth;
        bool IContentTreeNode.CanExpand => IsDir && HasChildren;
        bool IContentTreeNode.Loading => Loading;
        string IContentTreeNode.Error => Error;
        bool IContentTreeNode.More => More;
        IEnumerable<IContentTreeNode> IContentTreeNode.Children => Children;

        // Against the project's own directory. Null for the root itself, which has no relative
        // path worth the name, and for anything that somehow sits outside it.
        public string Relative
        {
            get
            {
                string root = Root;
                if (string.IsNullOrEmpty(root)) return null;
                if (root == "/")
                    return Path.StartsWith("/", StringComparison.Ordinal) && Path.Length > 1
                        ? Path.Substring(1) : null;
                root = root.TrimEnd('/');
                if (Path.Length <= root.Length + 1) return null;
                return Path.StartsWith(root + "/", StringComparison.Ordinal) ? Path.Substring(root.Length + 1) : null;
            }
        }
    }
}
