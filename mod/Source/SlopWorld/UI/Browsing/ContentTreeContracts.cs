using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Files and Git supply feature-owned nodes and groups. These data contracts are
    // independent of ContentTreeView's geometry, drawing and input handling.
    public interface IContentTreeNode
    {
        string Name { get; }
        string Key { get; }
        string ScopeKey { get; }
        bool IsDirectory { get; }
        int Depth { get; }
        bool CanExpand { get; }
        bool Loading { get; }
        string Error { get; }
        bool More { get; }
        IEnumerable<IContentTreeNode> Children { get; }
    }

    public sealed class ContentTreeGroup
    {
        public readonly string Key;
        public string ParentKey;
        public bool MainCheckout;
        public string ScopeKey;
        public readonly string Label;
        public readonly string Path;
        public readonly object Value;
        public readonly IContentTreeNode Root;

        public ContentTreeGroup(
            string key,
            string label,
            string path,
            object value,
            IContentTreeNode root
        )
        {
            Key = key;
            Label = label;
            Path = path;
            Value = value;
            Root = root;
        }

        // Fold identity belongs to the visible heading; scope identity continues to own
        // file actions and refreshes when the lone main checkout shares its project row.
        public static IList<ContentTreeGroup> Checkouts(
            IEnumerable<BrowseScope> scopes,
            Func<BrowseScope, ContentTreeGroup> create,
            bool showRootBranch = false
        )
        {
            var groups = new List<ContentTreeGroup>();
            foreach (var project in scopes.GroupBy(s => s.ProjectKey))
            {
                var first = project.First();
                bool merged = project.Count() == 1 && first.Worktree == "main";
                if (!merged)
                    groups.Add(
                        new ContentTreeGroup(first.ProjectKey, first.Project, "", null, null)
                    );
                foreach (var scope in project)
                {
                    var checkout = create(scope);
                    string label = merged ? scope.Project : checkout.Label;
                    if (showRootBranch && scope.Worktree == "main" && checkout.Label.Length > 0)
                        label = merged
                            ? scope.Project + "  · " + checkout.Label
                            : "· " + checkout.Label;
                    groups.Add(
                        new ContentTreeGroup(
                            merged ? scope.ProjectKey : scope.Key,
                            label,
                            checkout.Path,
                            checkout.Value,
                            checkout.Root
                        )
                        {
                            ParentKey = merged ? null : scope.ProjectKey,
                            ScopeKey = scope.Key,
                            MainCheckout = scope.Worktree == "main",
                        }
                    );
                }
            }
            return groups;
        }

        public static string CheckoutLabel(BrowseScope scope, string branch)
        {
            string name = scope.Worktree == "main" ? "" : scope.Label;
            return string.IsNullOrEmpty(branch) ? name
                : name.Length == 0 ? branch
                : name + "  · " + branch;
        }
    }
}
