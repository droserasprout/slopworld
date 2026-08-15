using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The files and git tabs are different answers laid out in the same instrument: a set of
    // project bands, followed by an indented tree. The source supplies the answer-specific
    // rows and actions; this class owns the geometry, scroll view, selection and input pass.
    public interface IContentTreeNode
    {
        string Name { get; }
        string Key { get; }
        string Project { get; }
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

    // A view-specific source is deliberately about rows, not rendering the whole tree. The
    // default methods are the file-tree answers; Git overrides only its state line, tails and
    // menus. This keeps both tabs on one copy of the scroll and hit-test plumbing.
    public abstract class ContentTreeSource
    {
        public abstract IList<ContentTreeGroup> Groups();

        public virtual bool IsGroupCollapsed(ContentTreeGroup group) => false;
        public virtual void ToggleGroup(ContentTreeGroup group) { }
        public virtual string GroupTooltip(ContentTreeGroup group) => group.Path;
        public virtual float DrawGroupTail(Rect row, ContentTreeGroup group, float right) => right;

        public virtual float GroupBodyHeight(ContentTreeGroup group) => 0f;
        public virtual float DrawGroupBody(float width, float y, ContentTreeGroup group) => y;

        public virtual void EnsureLoaded(IContentTreeNode node) { }
        public virtual bool IsExpanded(IContentTreeNode node) => node.IsDirectory;
        public virtual void ToggleNode(IContentTreeNode node) { }

        public virtual RowAct Actions(IContentTreeNode node) => RowAct.None;
        public virtual float DrawRowTail(Rect row, IContentTreeNode node, float right) => right;
        public virtual string RowTooltip(IContentTreeNode node) => null;

        public virtual void Open(IContentTreeNode node) { }
        public virtual void Action(IContentTreeNode node, RowAct action) { }
        public virtual List<FloatMenuOption> GroupMenu(ContentTreeGroup group) => null;
        public virtual List<FloatMenuOption> RowMenu(IContentTreeNode node) => null;
        public virtual string SelectionKey(IContentTreeNode node) =>
            ContentTreeView.SelectionKey(node.Project, node.Key);
    }

    public sealed class ContentTreeView
    {
        static float RowH => SlopWidgets.TinyRowH;
        const float IconW = 16f;
        const float Indent = 11f;
        const float Pad = SlopWidgets.GapS;
        const float CellX = SlopWidgets.GapS;
        const float ArrowW = 11f;

        readonly ContentTreeSource _source;
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly List<Line> _lines = new List<Line>();
        Rect _body;
        string _selected;

        struct Line
        {
            public ContentTreeGroup Group;
            public IContentTreeNode Node;
            public Rect Rect;
        }

        public ContentTreeView(ContentTreeSource source)
        {
            _source = source;
        }

        public static string SelectionKey(string project, string path) =>
            (project ?? "") + "\n" + (path ?? "");

        public void Draw(Rect body)
        {
            _body = body;
            _lines.Clear();

            var groups = _source.Groups();
            if (groups.Count == 0)
            {
                ViewChrome.Empty(body);
                return;
            }

            float height = Measure(groups);
            var view = new Rect(0f, 0f,
                body.width - (height > body.height ? SlopWidgets.ScrollbarW : 0f), height);

            // GUI rather than GUILayout: AgentSidebar calls this during its non-Layout back
            // pass. The finally is important because a missed End would move every later
            // window into the tree's scroll group.
            _scroll.Begin(body, view);
            try
            {
                float y = Pad;
                foreach (var group in groups)
                    y = DrawGroup(view.width, y, group);
            }
            finally
            {
                _scroll.End();
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
        }

        float Measure(IList<ContentTreeGroup> groups)
        {
            float height = Pad * 2f;
            foreach (var group in groups)
            {
                height += RowH;
                if (_source.IsGroupCollapsed(group)) continue;
                height += _source.GroupBodyHeight(group);
                if (group.Root != null) height += Count(group.Root) * RowH;
            }
            return height;
        }

        float Count(IContentTreeNode node)
        {
            if (!_source.IsExpanded(node)) return 0f;
            var children = node.Children;
            if (children == null) return 1f;

            float count = 0f;
            foreach (var child in children)
            {
                count += 1f;
                if (child.IsDirectory) count += Count(child);
            }
            if (node.More) count += 1f;
            return count;
        }

        float DrawGroup(float width, float y, ContentTreeGroup group)
        {
            var row = new Rect(0f, y, width, RowH);
            bool collapsed = _source.IsGroupCollapsed(group);

            SlopWidgets.HoverRow(row);
            GUI.color = SlopWidgets.Faint;
            var arrow = new Rect(CellX, row.y + (RowH - ArrowW) / 2f, ArrowW, ArrowW);
            GUI.DrawTexture(arrow, collapsed ? TexButton.Reveal : TexButton.Collapse);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float right = _source.DrawGroupTail(row, group, row.width - CellX);
            float left = arrow.xMax + 4f;
            var label = new Rect(left, row.y, Mathf.Max(0f, right - left), RowH);
            SlopWidgets.RowLabel(label, group.Label);

            Slab.Hairline(new Rect(CellX, row.yMax - 1f, row.width - CellX * 2f, 1f),
                SlopWidgets.Edge);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            string tip = _source.GroupTooltip(group);
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(row, tip + "\n\nClick to fold.");
            _lines.Add(new Line { Group = group, Rect = row });
            y += RowH;

            if (collapsed) return y;
            y = _source.DrawGroupBody(width, y, group);
            return group.Root == null ? y : DrawRows(width, y, group.Root);
        }

        float DrawRows(float width, float y, IContentTreeNode parent)
        {
            _source.EnsureLoaded(parent);
            if (!_source.IsExpanded(parent)) return y;

            var children = parent.Children;
            if (children == null)
            {
                return ViewChrome.Note(width, y, parent.Depth + 1,
                    parent.Error ?? "...",
                    parent.Error != null ? SlopWidgets.Bad : SlopWidgets.Faint);
            }

            foreach (var node in children)
            {
                y = DrawRow(width, y, node);
                if (node.IsDirectory) y = DrawRows(width, y, node);
            }
            if (parent.More)
                y = ViewChrome.Note(width, y, parent.Depth + 1,
                    "... more, not listed", SlopWidgets.Faint);
            return y;
        }

        float DrawRow(float width, float y, IContentTreeNode node)
        {
            var row = new Rect(0f, y, width, RowH);
            bool over = SlopWidgets.HoverRow(row);
            if (IsSelected(node)) Slab.Fill(row, SlopWidgets.Hover);

            float x = CellX + node.Depth * Indent;
            if (node.IsDirectory && node.CanExpand)
            {
                GUI.color = SlopWidgets.Faint;
                GUI.DrawTexture(new Rect(x, y + (RowH - ArrowW) / 2f, ArrowW, ArrowW),
                    _source.IsExpanded(node) ? TexButton.Collapse : TexButton.Reveal);
                GUI.color = Color.white;
            }
            x += ArrowW + 3f;

            var icon = FileIcons.Of(node.Name, node.IsDirectory);
            if (icon != null)
                GUI.DrawTexture(new Rect(x, y + (RowH - IconW) / 2f, IconW, IconW), icon);
            x += IconW + 5f;

            Text.Font = GameFont.Tiny;
            float right = width - Pad;
            RowAct acts = over ? _source.Actions(node) : RowAct.None;
            if (acts != RowAct.None)
                right = RowActions.Draw(row, right, acts) - 4f;
            else
                right = _source.DrawRowTail(row, node, right);

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = node.IsDirectory ? SlopWidgets.Lead : SlopWidgets.Name;
            SlopWidgets.RowLabel(new Rect(x, y, Mathf.Max(0f, right - x - 2f), RowH), node.Name);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            if (!string.IsNullOrEmpty(_source.RowTooltip(node)) &&
                RowActions.Hit(row, width - Pad, acts) == RowAct.None)
                TooltipHandler.TipRegion(row, _source.RowTooltip(node));

            _lines.Add(new Line { Node = node, Rect = row });
            return y + RowH;
        }

        public bool IsSelected(IContentTreeNode node) =>
            _selected != null && _selected == _source.SelectionKey(node);

        public void Select(IContentTreeNode node) =>
            _selected = node == null ? null : _source.SelectionKey(node);

        public void SelectKey(string key) => _selected = key;
        public void ClearSelection() => _selected = null;
        public void JumpTo(Vector2 position) => _scroll.JumpTo(position);

        public void Clicks(Action releaseViewer)
        {
            if (!ColonistBarStrip.Interactive) return;
            var e = Event.current;
            if (e.rawType != EventType.MouseDown || (e.button != 0 && e.button != 1)) return;

            foreach (var line in _lines)
            {
                if (!ColonistBarStrip.MouseOver(Screen(line.Rect))) continue;

                if (line.Group != null)
                {
                    if (e.button == 0) _source.ToggleGroup(line.Group);
                    else OpenMenu(_source.GroupMenu(line.Group));
                    ClearSelection();
                    releaseViewer();
                }
                else if (e.button == 1)
                {
                    OpenMenu(_source.RowMenu(line.Node));
                }
                else
                {
                    var screen = Screen(line.Rect);
                    var action = RowActions.Hit(screen, screen.xMax - Pad,
                        _source.Actions(line.Node));
                    if (action != RowAct.None)
                        _source.Action(line.Node, action);
                    else if (line.Node.IsDirectory)
                    {
                        _source.ToggleNode(line.Node);
                        ClearSelection();
                        releaseViewer();
                    }
                    else
                    {
                        Select(line.Node);
                        _source.Open(line.Node);
                    }
                }

                e.Use();
                return;
            }
        }

        void OpenMenu(List<FloatMenuOption> options)
        {
            if (options != null) TerminalWindow.OpenOverPane(new SlopMenu(options));
        }

        Rect Screen(Rect row)
        {
            var moved = new Rect(_body.x + row.x - _scroll.Position.x,
                _body.y + row.y - _scroll.Position.y, row.width, row.height);
            return moved.yMax <= _body.y || moved.y >= _body.yMax ? Rect.zero : moved;
        }
    }
}
