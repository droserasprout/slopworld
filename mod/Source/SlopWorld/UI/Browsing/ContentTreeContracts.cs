using System.Collections.Generic;

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
        public readonly string Label;
        public readonly string Path;
        public readonly object Value;
        public readonly IContentTreeNode Root;

        public ContentTreeGroup(string key, string label, string path, object value,
            IContentTreeNode root)
        {
            Key = key;
            Label = label;
            Path = path;
            Value = value;
            Root = root;
        }
    }

}
