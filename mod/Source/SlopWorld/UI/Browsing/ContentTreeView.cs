using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public interface IContentTreeLoader
    {
        void EnsureLoaded(IContentTreeNode node);
    }

    public interface IContentTreeGroupExtras
    {
        float DrawGroupTail(Rect row, ContentTreeGroup group, float right);
        GroupAct GroupActions(ContentTreeGroup group);
        void GroupAction(ContentTreeGroup group, GroupAct action);
        float GroupBodyHeight(ContentTreeGroup group);
        float DrawGroupBody(float width, float y, ContentTreeGroup group);
    }

    public interface IContentTreeRowActions
    {
        RowAct Actions(IContentTreeNode node);
        float DrawRowTail(Rect row, IContentTreeNode node, float right);
        string RowTooltip(IContentTreeNode node);
        void Action(IContentTreeNode node, RowAct action);
    }

    public interface IContentTreeSelection
    {
        void DoubleClick(IContentTreeNode node);
    }

    // A view-specific source is deliberately about rows, not rendering the whole tree. The
    // required contract covers structure and primary navigation. Optional capabilities are
    // explicit so a source cannot silently advertise unsupported no-op behavior.
    public abstract class ContentTreeSource
    {
        public abstract IList<ContentTreeGroup> Groups();
        public abstract string EmptyReason { get; }

        // A source increments this when the visible tree shape or any cached row state changes.
        // ContentTreeView keeps the flattened row index until then, so a large expanded tree is
        // not recursively measured and walked again on every repaint.
        public abstract int Revision { get; }

        public abstract bool IsGroupCollapsed(ContentTreeGroup group);
        public abstract void ToggleGroup(ContentTreeGroup group);
        public abstract string GroupTooltip(ContentTreeGroup group);

        public abstract bool IsExpanded(IContentTreeNode node);
        public abstract void ToggleNode(IContentTreeNode node);
        public abstract Color RowIconColor(IContentTreeNode node);
        public abstract Color RowLabelColor(IContentTreeNode node);
        public abstract void Open(IContentTreeNode node);
        public abstract List<FloatMenuOption> GroupMenu(ContentTreeGroup group);
        public abstract List<FloatMenuOption> RowMenu(IContentTreeNode node);
    }

    public sealed class ContentTreeView
    {
        static float RowH => UiTheme.TinyRowH;
        const float IconW = 16f;
        const float Indent = 11f;
        static float Pad => UiTheme.GapS;
        static float CellX => UiTheme.GapS;
        const float ArrowW = UiTheme.DisclosureW;

        readonly ContentTreeSource _source;
        readonly ContentTreeController _controller;
        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly MouseClickSequence _clicks = new MouseClickSequence();
        readonly List<Line> _lines = new List<Line>();
        readonly List<Item> _items = new List<Item>();
        readonly ContentTreeIndex _index = new ContentTreeIndex();
        IList<ContentTreeGroup> _groups;
        Rect _body;
        string _clickKey;
        string _reveal;
        float _revealTop = -1f;
        float _visibleTop;
        float _visibleBottom;
        float _contentHeight = -1f;
        int _groupsRevision = int.MinValue;
        int _workspaceRevision = int.MinValue;

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

        public ContentTreeView(ContentTreeSource source, ContentTreeController controller)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public static string SelectionKey(string project, string path) =>
            (project ?? "") + "\n" + (path ?? "");

        public void Draw(Rect body, bool anchorBoundary = true)
        {
            if (_scroll.HandleWheel(body, _contentHeight)) return;
            using (WidgetState.Save())
            {
                // Routed pager headers grow above this viewport. Keep tree rows at their
                // previous screen positions as that space is added or removed. A live Files
                // divider drag deliberately lets the viewport and its rows move together.
                if (anchorBoundary && _body.height > 0f && body.y != _body.y)
                    _scroll.JumpTo(new Vector2(_scroll.Position.x,
                        ContentTreeIndex.AnchoredScroll(_scroll.Position.y, _body.y, body.y)));
                _body = body;
                _lines.Clear();

                var groups = Groups();
                if (groups.Count == 0)
                {
                    ViewChrome.Empty(body, _source.EmptyReason);
                    return;
                }

                EnsureLayout(groups);
                if (_reveal != null && _index.Reveal(_reveal, out float revealTop))
                {
                    _revealTop = revealTop;
                    _reveal = null;
                }
                float height = _contentHeight;
                var geometry = UiScrollBody.Measure(body, height,
                    UiScrollbarReservation.WhenNeeded);
                var view = geometry.View;
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
                    // Scroll events can arrive in a burst. They only need to update the offset.
                    // painting thousands of tree rows for each queued event makes the input queue
                    // take seconds to drain.
                    if (!SmoothScroll.WheelOnly)
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
            int workspaceRevision = WorkspaceLayout.Revision;
            if (_index.IsCurrent(revision) && _workspaceRevision == workspaceRevision)
                return;

            _items.Clear();
            _index.Clear();

            float y = Pad;
            foreach (var group in groups)
            {
                if (group.ParentKey != null && _controller.IsGroupCollapsed(group.ParentKey)) continue;
                _items.Add(new Item { Kind = ItemKind.Group, Group = group, Y = y });
                y += RowH;
                if (_source.IsGroupCollapsed(group)) continue;

                var extras = _source as IContentTreeGroupExtras;
                float bodyHeight = extras == null ? 0f : extras.GroupBodyHeight(group);
                if (bodyHeight > 0f)
                {
                    _items.Add(new Item { Kind = ItemKind.Body, Group = group, Y = y });
                    y += bodyHeight;
                }

                if (group.Root != null) BuildRows(group.Root, ref y, group.ParentKey == null ? 0 : 1 - group.Root.Depth);
            }

            _contentHeight = y + Pad;
            for (int i = 0; i < _items.Count; i++)
            {
                float end = i + 1 < _items.Count ? _items[i + 1].Y : y;
                var item = _items[i];
                item.Height = end - item.Y;
                _items[i] = item;
                _index.Add(item.Y, end,
                    item.Node == null ? null : SelectionKey(item.Node));
            }
            _index.Commit(revision);
            _workspaceRevision = workspaceRevision;
        }

        void BuildRows(IContentTreeNode parent, ref float y, int indent)
        {
            var loader = _source as IContentTreeLoader;
            if (loader != null) loader.EnsureLoaded(parent);
            if (!_source.IsExpanded(parent)) return;

            var children = parent.Children;
            if (children == null)
            {
                _items.Add(new Item
                {
                    Kind = ItemKind.Note,
                    Note = parent.Error ?? "Loading",
                    NoteColor = parent.Error != null ? UiTheme.Bad : UiTheme.Faint,
                    Depth = parent.Depth + 1 + indent,
                    Y = y,
                });
                y += RowH;
                return;
            }

            foreach (var child in children)
            {
                _items.Add(new Item { Kind = ItemKind.Node, Node = child, Depth = child.Depth + indent, Y = y });
                y += RowH;
                if (child.IsDirectory) BuildRows(child, ref y, indent);
            }

            if (parent.More)
            {
                _items.Add(new Item
                {
                    Kind = ItemKind.Note,
                    Note = "More entries are not shown.",
                    NoteColor = UiTheme.Faint,
                    Depth = parent.Depth + 1 + indent,
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
                    var extras = _source as IContentTreeGroupExtras;
                    if (extras != null) extras.DrawGroupBody(width, item.Y, item.Group);
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
                DrawRow(width, item.Y, item.Node, item.Depth);
            }
        }

        void DrawGroup(float width, float y, ContentTreeGroup group)
        {
            using (WidgetState.Save())
            {
                var row = new Rect(0f, y, width, RowH);
                bool collapsed = _source.IsGroupCollapsed(group);

                bool over = RowChrome.Hover(row, false, true, RowHoverPolicy.OverlayAware);
                GUI.color = UiTheme.Faint;
                var arrow = new Rect(CellX + (group.ParentKey == null ? 0f : Indent), row.y + (RowH - ArrowW) / 2f, ArrowW, ArrowW);
                GUI.DrawTexture(arrow, collapsed ? TexButton.Reveal : TexButton.Collapse);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                var extras = _source as IContentTreeGroupExtras;
                GroupAct acts = over && extras != null
                    ? extras.GroupActions(group) : GroupAct.None;
                float right = row.width - CellX;
                if (acts != GroupAct.None)
                    right = GroupActions.Draw(row, right, acts) - UiTheme.GapXS;
                else if (extras != null)
                    right = extras.DrawGroupTail(row, group, right);
                float left = arrow.xMax + UiTheme.GapXS;
                var label = new Rect(left, row.y, Mathf.Max(0f, right - left), RowH);
                UiText.RowLabel(label, group.Label);

                Slab.Hairline(new Rect(CellX, row.yMax - 1f, row.width - CellX * 2f, 1f),
                    UiTheme.Edge);

                string tip = _source.GroupTooltip(group);
                if (!string.IsNullOrEmpty(tip) && GroupActions.Hit(row, row.width - CellX, acts)
                    == GroupAct.None)
                    TooltipHandler.TipRegion(row, tip + "\n\nClick to fold.");
                _lines.Add(new Line { Group = group, Rect = row });
            }
        }

        bool Visible(float y) => y + RowH > _visibleTop && y < _visibleBottom;

        float DrawRow(float width, float y, IContentTreeNode node, int depth)
        {
            using (WidgetState.Save())
            {
                var row = new Rect(0f, y, width, RowH);
                bool over = RowChrome.Hover(row, IsSelected(node), true,
                    RowHoverPolicy.OverlayAware, RowSelectionStyle.Hover);

                float x = CellX + depth * Indent;
                if (node.IsDirectory && node.CanExpand)
                {
                    GUI.color = UiTheme.Faint;
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
                var actions = _source as IContentTreeRowActions;
                RowAct acts = over && actions != null ? actions.Actions(node) : RowAct.None;
                if (acts != RowAct.None)
                    right = RowActions.Draw(row, right, acts) - UiTheme.GapXS;
                else if (actions != null)
                    right = actions.DrawRowTail(row, node, right);

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = _source.RowLabelColor(node);
                UiText.RowLabel(new Rect(x, y, Mathf.Max(0f, right - x - 2f), RowH), node.Name);

                string tooltip = actions == null ? null : actions.RowTooltip(node);
                if (!string.IsNullOrEmpty(tooltip) &&
                    RowActions.Hit(row, width - Pad, acts) == RowAct.None)
                    TooltipHandler.TipRegion(row, tooltip);

                _lines.Add(new Line { Node = node, Rect = row });
                return y + RowH;
            }
        }

        string SelectionKey(IContentTreeNode node) =>
            SelectionKey(node.ScopeKey, node.Key);

        public bool IsSelected(IContentTreeNode node) =>
            _controller.IsSelected(SelectionKey(node));

        public void Select(IContentTreeNode node) =>
            _controller.Select(node == null ? null : SelectionKey(node));

        public void SelectKey(string key) => _controller.Select(key);
        public void RevealKey(string key)
        {
            _controller.Select(key);
            _reveal = key;
        }
        public void ClearSelection()
        {
            _controller.ClearSelection();
            _reveal = null;
            _revealTop = -1f;
        }
        public void JumpTo(Vector2 position) => _scroll.JumpTo(position);

        public void Clicks()
        {
            if (!ColonistBarStrip.Interactive) return;
            var e = Event.current;
            if (e.rawType != EventType.MouseDown || (e.button != 0 && e.button != 1)) return;

            foreach (var line in _lines)
            {
                if (!ColonistBarStrip.MouseOver(ClippedScreen(line.Rect))) continue;

                if (line.Group != null)
                {
                    ResetClicks();
                    if (e.button == 0)
                    {
                        var screen = Screen(line.Rect);
                        var extras = _source as IContentTreeGroupExtras;
                        GroupAct groupActs = extras == null
                            ? GroupAct.None : extras.GroupActions(line.Group);
                        var action = GroupActions.Hit(screen, screen.xMax - CellX, groupActs);
                        if (action != GroupAct.None && extras != null)
                            extras.GroupAction(line.Group, action);
                        else
                            _source.ToggleGroup(line.Group);
                    }
                    else OpenMenu(_source.GroupMenu(line.Group));
                    ClearSelection();
                    // Folding preserves the shared reader pane.
                }
                else if (e.button == 1)
                {
                    ResetClicks();
                    OpenMenu(_source.RowMenu(line.Node));
                }
                else
                {
                    var screen = Screen(line.Rect);
                    var actions = _source as IContentTreeRowActions;
                    RowAct rowActs = actions == null ? RowAct.None : actions.Actions(line.Node);
                    var action = RowActions.Hit(screen, screen.xMax - Pad, rowActs);
                    if (action != RowAct.None && actions != null)
                    {
                        ResetClicks();
                        actions.Action(line.Node, action);
                    }
                    else if (line.Node.IsDirectory)
                    {
                        ResetClicks();
                        _source.ToggleNode(line.Node);
                        ClearSelection();
                        // Expanding or collapsing a directory only changes the tree shape.
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
                            var selection = _source as IContentTreeSelection;
                            if (selection != null) selection.DoubleClick(line.Node);
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
            string key = SelectionKey(node);
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

        Rect ClippedScreen(Rect row)
        {
            var screen = Screen(row);
            float left = Mathf.Max(screen.xMin, _body.xMin);
            float top = Mathf.Max(screen.yMin, _body.yMin);
            float right = Mathf.Min(screen.xMax, _body.xMax);
            float bottom = Mathf.Min(screen.yMax, _body.yMax);
            return right <= left || bottom <= top ? Rect.zero : new Rect(left, top, right - left, bottom - top);
        }

        Rect Screen(Rect row)
        {
            var moved = new Rect(_body.x + row.x - _scroll.Position.x,
                _body.y + row.y - _scroll.Position.y, row.width, row.height);
            return moved;
        }
    }
}
