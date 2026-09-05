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
        public virtual GroupAct GroupActions(ContentTreeGroup group) => GroupAct.None;
        public virtual void GroupAction(ContentTreeGroup group, GroupAct action) { }

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
        const float ArrowW = SlopWidgets.DisclosureW;

        readonly ContentTreeSource _source;
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly List<Line> _lines = new List<Line>();
        Rect _body;
        string _selected;
        string _reveal;
        float _revealTop = -1f;
        float _visibleTop;
        float _visibleBottom;
        float _contentHeight = -1f;

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
            using (WidgetState.Save())
            {
                _body = body;
                _lines.Clear();

                var groups = _source.Groups();
                if (groups.Count == 0)
                {
                    ViewChrome.Empty(body);
                    return;
                }

                bool scrollEvent = Event.current.type == EventType.ScrollWheel ||
                    (Event.current.type == EventType.Used &&
                        Event.current.rawType == EventType.ScrollWheel);
                // A wheel burst does not change tree shape. Reusing the last measured height
                // avoids walking every expanded directory just to consume another input event.
                float height = scrollEvent && _contentHeight >= 0f
                    ? _contentHeight : Measure(groups);
                _contentHeight = height;
                var view = new Rect(0f, 0f,
                    body.width - (height > body.height ? SlopWidgets.ScrollbarW : 0f), height);
                if (_revealTop >= 0f)
                {
                    _scroll.Reveal(_revealTop, RowH, body.height);
                    _revealTop = -1f;
                }

                // GUI rather than GUILayout: AgentSidebar calls this during its non-Layout back
                // pass. The finally is important because a missed End would move every later
                // window into the tree's scroll group.
                using (_scroll.Scope(body, view))
                {
                    _visibleTop = _scroll.Position.y - RowH;
                    _visibleBottom = _scroll.Position.y + body.height + RowH;
                    // Scroll events can arrive in a burst. They only need to update the offset;
                    // painting thousands of tree rows for each queued event makes the input queue
                    // take seconds to drain.
                    if (!scrollEvent)
                    {
                        float y = Pad;
                        foreach (var group in groups)
                            y = DrawGroup(view.width, y, group);
                    }
                }
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
            using (WidgetState.Save())
            {
                var row = new Rect(0f, y, width, RowH);
                bool collapsed = _source.IsGroupCollapsed(group);

                bool over = RowChrome.Hover(row, false, true, RowHoverPolicy.OverlayAware);
                GUI.color = SlopWidgets.Faint;
                var arrow = new Rect(CellX, row.y + (RowH - ArrowW) / 2f, ArrowW, ArrowW);
                GUI.DrawTexture(arrow, collapsed ? TexButton.Reveal : TexButton.Collapse);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GroupAct acts = over ? _source.GroupActions(group) : GroupAct.None;
                float right = row.width - CellX;
                if (acts != GroupAct.None)
                    right = GroupActions.Draw(row, right, acts) - 4f;
                else
                    right = _source.DrawGroupTail(row, group, right);
                float left = arrow.xMax + 4f;
                var label = new Rect(left, row.y, Mathf.Max(0f, right - left), RowH);
                SlopWidgets.RowLabel(label, group.Label);

                Slab.Hairline(new Rect(CellX, row.yMax - 1f, row.width - CellX * 2f, 1f),
                    SlopWidgets.Edge);

                string tip = _source.GroupTooltip(group);
                if (!string.IsNullOrEmpty(tip) && GroupActions.Hit(row, row.width - CellX, acts)
                    == GroupAct.None)
                    TooltipHandler.TipRegion(row, tip + "\n\nClick to fold.");
                _lines.Add(new Line { Group = group, Rect = row });
                y += RowH;

                if (collapsed) return y;
                y = _source.DrawGroupBody(width, y, group);
                return group.Root == null ? y : DrawRows(width, y, group.Root);
            }
        }

        float DrawRows(float width, float y, IContentTreeNode parent)
        {
            _source.EnsureLoaded(parent);
            if (!_source.IsExpanded(parent)) return y;

            var children = parent.Children;
            if (children == null)
            {
                return Visible(y)
                    ? ViewChrome.Note(width, y, parent.Depth + 1,
                        parent.Error ?? "...",
                        parent.Error != null ? SlopWidgets.Bad : SlopWidgets.Faint)
                    : y + RowH;
            }

            foreach (var node in children)
            {
                bool reveal = _reveal != null && _reveal == _source.SelectionKey(node);
                if (Visible(y) || reveal) DrawRow(width, y, node);
                y += RowH;
                if (node.IsDirectory) y = DrawRows(width, y, node);
            }
            if (parent.More)
                y = Visible(y)
                    ? ViewChrome.Note(width, y, parent.Depth + 1,
                        "... more, not listed", SlopWidgets.Faint)
                    : y + RowH;
            return y;
        }

        bool Visible(float y) => y + RowH > _visibleTop && y < _visibleBottom;

        float DrawRow(float width, float y, IContentTreeNode node)
        {
            using (WidgetState.Save())
            {
                var row = new Rect(0f, y, width, RowH);
                bool over = RowChrome.Hover(row, IsSelected(node), true,
                    RowHoverPolicy.OverlayAware, RowSelectionStyle.Hover);

                float x = CellX + node.Depth * Indent;
                if (node.IsDirectory && node.CanExpand)
                {
                    GUI.color = SlopWidgets.Faint;
                    GUI.DrawTexture(new Rect(x, y + (RowH - ArrowW) / 2f, ArrowW, ArrowW),
                        _source.IsExpanded(node) ? TexButton.Collapse : TexButton.Reveal);
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

                if (!string.IsNullOrEmpty(_source.RowTooltip(node)) &&
                    RowActions.Hit(row, width - Pad, acts) == RowAct.None)
                    TooltipHandler.TipRegion(row, _source.RowTooltip(node));

                _lines.Add(new Line { Node = node, Rect = row });
                if (_reveal != null && _reveal == _source.SelectionKey(node))
                {
                    // Apply before Begin on the next pass. Changing the scroll transform inside
                    // its GUI group would make this pass's drawing and hit testing disagree.
                    _revealTop = row.y;
                    _reveal = null;
                }
                return y + RowH;
            }
        }

        public bool IsSelected(IContentTreeNode node) =>
            _selected != null && _selected == _source.SelectionKey(node);

        public void Select(IContentTreeNode node) =>
            _selected = node == null ? null : _source.SelectionKey(node);

        public void SelectKey(string key) => _selected = key;
        public void RevealKey(string key)
        {
            _selected = key;
            _reveal = key;
        }
        public void ClearSelection()
        {
            _selected = null;
            _reveal = null;
            _revealTop = -1f;
        }
        public void JumpTo(Vector2 position) => _scroll.JumpTo(position);

        public void Clicks(Action releaseViewer = null)
        {
            if (!ColonistBarStrip.Interactive) return;
            var e = Event.current;
            if (e.rawType != EventType.MouseDown || (e.button != 0 && e.button != 1)) return;

            foreach (var line in _lines)
            {
                if (!ColonistBarStrip.MouseOver(Screen(line.Rect))) continue;

                if (line.Group != null)
                {
                    if (e.button == 0)
                    {
                        var screen = Screen(line.Rect);
                        var action = GroupActions.Hit(screen, screen.xMax - CellX,
                            _source.GroupActions(line.Group));
                        if (action != GroupAct.None)
                            _source.GroupAction(line.Group, action);
                        else
                            _source.ToggleGroup(line.Group);
                    }
                    else OpenMenu(_source.GroupMenu(line.Group));
                    ClearSelection();
                    releaseViewer?.Invoke();
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
                        // Expanding or collapsing a directory only changes the tree shape;
                        // keep a file preview open while the user navigates around it.
                    }
                    else
                    {
                        // The source owns selection because its Open method needs to compare
                        // the old row with the clicked one before replacing the viewer.
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
