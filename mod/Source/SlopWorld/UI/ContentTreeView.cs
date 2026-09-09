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

        // A source increments this when the visible tree shape or any cached row state changes.
        // ContentTreeView keeps the flattened row index until then, so a large expanded tree is
        // not recursively measured and walked again on every repaint.
        public virtual int Revision => 0;

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
        public virtual Color RowIconColor(IContentTreeNode node) => Color.white;
        public virtual Color RowLabelColor(IContentTreeNode node) =>
            node.IsDirectory ? UiWidgets.Lead : UiWidgets.Name;
        public virtual float DrawRowTail(Rect row, IContentTreeNode node, float right) => right;
        public virtual string RowTooltip(IContentTreeNode node) => null;

        public virtual void Open(IContentTreeNode node) { }
        // Sources may use a second body click to pin the preview just opened by Open.
        public virtual void DoubleClick(IContentTreeNode node) { }
        public virtual void Action(IContentTreeNode node, RowAct action) { }
        public virtual List<FloatMenuOption> GroupMenu(ContentTreeGroup group) => null;
        public virtual List<FloatMenuOption> RowMenu(IContentTreeNode node) => null;
        public virtual string SelectionKey(IContentTreeNode node) =>
            ContentTreeView.SelectionKey(node.Project, node.Key);
    }

    public sealed class ContentTreeView
    {
        static float RowH => UiWidgets.TinyRowH;
        const float IconW = 16f;
        const float Indent = 11f;
        const float Pad = UiWidgets.GapS;
        const float CellX = UiWidgets.GapS;
        const float ArrowW = UiWidgets.DisclosureW;

        readonly ContentTreeSource _source;
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly MouseClickSequence _clicks = new MouseClickSequence();
        readonly List<Line> _lines = new List<Line>();
        readonly List<Item> _items = new List<Item>();
        readonly ContentTreeIndex _index = new ContentTreeIndex();
        IList<ContentTreeGroup> _groups;
        Rect _body;
        string _selected;
        string _clickKey;
        string _reveal;
        float _revealTop = -1f;
        float _visibleTop;
        float _visibleBottom;
        float _contentHeight = -1f;
        int _groupsRevision = int.MinValue;

        enum ItemKind
        {
            Group,
            Body,
            Node,
            Note,
        }

        struct Item
        {
            public ItemKind Kind;
            public ContentTreeGroup Group;
            public IContentTreeNode Node;
            public string Note;
            public Color NoteColor;
            public int Depth;
            public float Y;
            public float Height;
        }

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

                var groups = Groups();
                if (groups.Count == 0)
                {
                    ViewChrome.Empty(body);
                    return;
                }

                EnsureLayout(groups);
                if (_reveal != null && _index.Reveal(_reveal, out float revealTop))
                {
                    _revealTop = revealTop;
                    _reveal = null;
                }
                bool scrollEvent = Event.current.type == EventType.ScrollWheel ||
                    (Event.current.type == EventType.Used &&
                        Event.current.rawType == EventType.ScrollWheel);
                float height = _contentHeight;
                var view = new Rect(0f, 0f,
                    UiScrollBody.ContentWidth(body, height), height);
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
                        for (int i = _index.First(_visibleTop);
                             i < _items.Count && _items[i].Y < _visibleBottom; i++)
                        {
                            PerfTrace.Count("content-tree-rows-visited");
                            DrawItem(view.width, _items[i]);
                        }
                    }
                }
            }
        }

        IList<ContentTreeGroup> Groups()
        {
            int revision = _source.Revision;
            if (_groups == null || _groupsRevision != revision)
            {
                _groups = _source.Groups();
                _groupsRevision = _source.Revision;
            }
            return _groups;
        }

        void EnsureLayout(IList<ContentTreeGroup> groups)
        {
            int revision = _source.Revision;
            if (_index.IsCurrent(revision)) return;

            _items.Clear();
            _index.Clear();

            float y = Pad;
            foreach (var group in groups)
            {
                _items.Add(new Item { Kind = ItemKind.Group, Group = group, Y = y });
                y += RowH;
                if (_source.IsGroupCollapsed(group)) continue;

                float bodyHeight = _source.GroupBodyHeight(group);
                if (bodyHeight > 0f)
                {
                    _items.Add(new Item { Kind = ItemKind.Body, Group = group, Y = y });
                    y += bodyHeight;
                }

                if (group.Root != null) BuildRows(group.Root, ref y);
            }

            _contentHeight = y + Pad;
            for (int i = 0; i < _items.Count; i++)
            {
                float end = i + 1 < _items.Count ? _items[i + 1].Y : y;
                var item = _items[i];
                item.Height = end - item.Y;
                _items[i] = item;
                _index.Add(item.Y, end,
                    item.Node == null ? null : _source.SelectionKey(item.Node));
            }
            _index.Commit(revision);
        }

        void BuildRows(IContentTreeNode parent, ref float y)
        {
            _source.EnsureLoaded(parent);
            if (!_source.IsExpanded(parent)) return;

            var children = parent.Children;
            if (children == null)
            {
                _items.Add(new Item
                {
                    Kind = ItemKind.Note,
                    Note = parent.Error ?? "...",
                    NoteColor = parent.Error != null ? UiWidgets.Bad : UiWidgets.Faint,
                    Depth = parent.Depth + 1,
                    Y = y,
                });
                y += RowH;
                return;
            }

            foreach (var child in children)
            {
                _items.Add(new Item { Kind = ItemKind.Node, Node = child, Y = y });
                y += RowH;
                if (child.IsDirectory) BuildRows(child, ref y);
            }

            if (parent.More)
            {
                _items.Add(new Item
                {
                    Kind = ItemKind.Note,
                    Note = "... more, not listed",
                    NoteColor = UiWidgets.Faint,
                    Depth = parent.Depth + 1,
                    Y = y,
                });
                y += RowH;
            }
        }

        void DrawItem(float width, Item item)
        {
            if (item.Kind == ItemKind.Group)
            {
                if (Visible(item.Y))
                {
                    PerfTrace.Count("content-tree-rows-drawn");
                    DrawGroup(width, item.Y, item.Group);
                }
                return;
            }
            if (item.Kind == ItemKind.Body)
            {
                if (item.Y + item.Height > _visibleTop && item.Y < _visibleBottom)
                {
                    PerfTrace.Count("content-tree-rows-drawn");
                    _source.DrawGroupBody(width, item.Y, item.Group);
                }
                return;
            }
            if (item.Kind == ItemKind.Note)
            {
                if (Visible(item.Y))
                {
                    PerfTrace.Count("content-tree-rows-drawn");
                    ViewChrome.Note(width, item.Y, item.Depth, item.Note, item.NoteColor);
                }
                return;
            }

            if (Visible(item.Y))
            {
                PerfTrace.Count("content-tree-rows-drawn");
                DrawRow(width, item.Y, item.Node);
            }
        }

        void DrawGroup(float width, float y, ContentTreeGroup group)
        {
            using (WidgetState.Save())
            {
                var row = new Rect(0f, y, width, RowH);
                bool collapsed = _source.IsGroupCollapsed(group);

                bool over = RowChrome.Hover(row, false, true, RowHoverPolicy.OverlayAware);
                GUI.color = UiWidgets.Faint;
                var arrow = new Rect(CellX, row.y + (RowH - ArrowW) / 2f, ArrowW, ArrowW);
                GUI.DrawTexture(arrow, collapsed ? TexButton.Reveal : TexButton.Collapse);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GroupAct acts = over ? _source.GroupActions(group) : GroupAct.None;
                float right = row.width - CellX;
                if (acts != GroupAct.None)
                    right = GroupActions.Draw(row, right, acts) - UiWidgets.GapXS;
                else
                    right = _source.DrawGroupTail(row, group, right);
                float left = arrow.xMax + UiWidgets.GapXS;
                var label = new Rect(left, row.y, Mathf.Max(0f, right - left), RowH);
                UiWidgets.RowLabel(label, group.Label);

                Slab.Hairline(new Rect(CellX, row.yMax - 1f, row.width - CellX * 2f, 1f),
                    UiWidgets.Edge);

                string tip = _source.GroupTooltip(group);
                if (!string.IsNullOrEmpty(tip) && GroupActions.Hit(row, row.width - CellX, acts)
                    == GroupAct.None)
                    TooltipHandler.TipRegion(row, tip + "\n\nClick to fold.");
                _lines.Add(new Line { Group = group, Rect = row });
            }
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
                    GUI.color = UiWidgets.Faint;
                    GUI.DrawTexture(new Rect(x, y + (RowH - ArrowW) / 2f, ArrowW, ArrowW),
                        _source.IsExpanded(node) ? TexButton.Collapse : TexButton.Reveal);
                }
                x += ArrowW + 3f;

                GUI.color = _source.RowIconColor(node);
                var icon = FileIcons.Of(node.Name, node.IsDirectory);
                if (icon != null)
                    GUI.DrawTexture(new Rect(x, y + (RowH - IconW) / 2f, IconW, IconW), icon);
                x += IconW + 5f;

                Text.Font = GameFont.Tiny;
                float right = width - Pad;
                RowAct acts = over ? _source.Actions(node) : RowAct.None;
                if (acts != RowAct.None)
                    right = RowActions.Draw(row, right, acts) - UiWidgets.GapXS;
                else
                    right = _source.DrawRowTail(row, node, right);

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = _source.RowLabelColor(node);
                UiWidgets.RowLabel(new Rect(x, y, Mathf.Max(0f, right - x - 2f), RowH), node.Name);

                if (!string.IsNullOrEmpty(_source.RowTooltip(node)) &&
                    RowActions.Hit(row, width - Pad, acts) == RowAct.None)
                    TooltipHandler.TipRegion(row, _source.RowTooltip(node));

                _lines.Add(new Line { Node = node, Rect = row });
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
                    ResetClicks();
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
                    ResetClicks();
                    OpenMenu(_source.RowMenu(line.Node));
                }
                else
                {
                    var screen = Screen(line.Rect);
                    var action = RowActions.Hit(screen, screen.xMax - Pad,
                        _source.Actions(line.Node));
                    if (action != RowAct.None)
                    {
                        ResetClicks();
                        _source.Action(line.Node, action);
                    }
                    else if (line.Node.IsDirectory)
                    {
                        ResetClicks();
                        _source.ToggleNode(line.Node);
                        ClearSelection();
                        // Expanding or collapsing a directory only changes the tree shape;
                        // keep a file preview open while the user navigates around it.
                    }
                    else
                    {
                        int clickCount = ObserveClick(line.Node, e);
                        // The source owns selection because its Open method needs to compare
                        // the old row with the clicked one before replacing the viewer.
                        _source.Open(line.Node);
                        if (clickCount >= 2)
                        {
                            _source.DoubleClick(line.Node);
                            ResetClicks();
                        }
                    }
                }

                e.Use();
                return;
            }
        }

        int ObserveClick(IContentTreeNode node, Event e)
        {
            string key = _source.SelectionKey(node);
            if (_clickKey != key)
            {
                _clicks.Reset();
                _clickKey = key;
            }
            return _clicks.Observe(e, Time.realtimeSinceStartup);
        }

        void ResetClicks()
        {
            _clicks.Reset();
            _clickKey = null;
        }

        void OpenMenu(List<FloatMenuOption> options)
        {
            if (options != null) TerminalWindow.OpenOverPane(new UiMenu(options));
        }

        Rect Screen(Rect row)
        {
            var moved = new Rect(_body.x + row.x - _scroll.Position.x,
                _body.y + row.y - _scroll.Position.y, row.width, row.height);
            return moved.yMax <= _body.y || moved.y >= _body.yMax ? Rect.zero : moved;
        }
    }
}
