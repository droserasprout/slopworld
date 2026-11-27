using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using static SlopWorld.BoardViewport;

namespace SlopWorld
{
    public sealed partial class BoardView
    {
        const float Handle = 32;
        readonly BoardViewport _viewport = new BoardViewport();
        string _panColumn;
        float _edgeTime;

        void DrawColumns(Rect frame, Wire.ProjectBoard board, bool narrow)
        {
            if (frame.height <= Header || frame.width <= 0)
                return;
            GUI.BeginGroup(frame);
            try
            {
                var area = new Rect(0, 0, frame.width, frame.height);
                float width = narrow ? Mathf.Max(100, area.width - 36) : 232;
                float limit = Mathf.Max(0, board.Columns.Count * (width + Gap) - Gap - area.width);
                _viewport.Scroll(0, 0, limit, null, area.height);
                var e = Event.current;
                var p = e.mousePosition;
                bool inside = Focused && area.Contains(p);
                Wire.BoardColumn ColumnAt(float x, out int index, out float inset)
                {
                    index = Mathf.FloorToInt((x + _viewport.Left) / (width + Gap));
                    inset = x + _viewport.Left - index * (width + Gap);
                    return inside && index >= 0 && index < board.Columns.Count && inset < width
                        ? board.Columns[index]
                        : null;
                }
                var hovered = ColumnAt(p.x, out _, out _);
                if (e.type == EventType.ScrollWheel && inside)
                {
                    var delta = e.shift ? new Vector2(e.delta.y * 36, 0) : e.delta * 36;
                    _viewport.Scroll(delta.x, delta.y, limit, hovered, area.height);
                    e.Use();
                }
                if (_gesture.Held && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
                {
                    _gesture.Cancel();
                    e.Use();
                }
                bool retarget = false;
                if (_gesture.Held && e.type == EventType.MouseDrag)
                {
                    _gesture.Motion(p.x, p.y);
                    if (_gesture.Panning)
                    {
                        var delta = _last - p;
                        _viewport.Scroll(
                            delta.x,
                            delta.y,
                            limit,
                            board.Columns.FirstOrDefault(c => c.Id == _panColumn),
                            area.height
                        );
                    }
                    _last = p;
                    retarget = true;
                    e.Use();
                }
                if (_gesture.Dragging && inside && e.type == EventType.Repaint)
                {
                    float now = Time.unscaledTime;
                    float step = Mathf.Min(.05f, now - _edgeTime) * 480;
                    retarget = _viewport.Scroll(
                        Edge(p.x, area.width) * step,
                        _gesture.Card != null ? Edge(p.y, area.height) * step : 0,
                        limit,
                        hovered,
                        area.height
                    );
                    _edgeTime = now;
                }
                var hit = ColumnAt(p.x, out int index, out float xInColumn);
                int row = Mathf.FloorToInt((p.y - Header + _viewport.Top(hit, area.height)) / Row);
                var card =
                    p.y >= Header && hit != null && row >= 0 && row < hit.Cards.Count
                        ? hit.Cards[row]
                        : null;
                bool header = p.y < Header;
                if (
                    inside
                    && e.type == EventType.MouseDown
                    && e.button == 0
                    && !_saving
                    && !_gesture.Held
                )
                {
                    if (hit != null && header && xInColumn > width - 42 && !board.Archived)
                        ColumnMenu(board, hit);
                    else
                    {
                        bool item = hit != null && (header || card != null);
                        _gesture.Press(
                            item ? hit.Id : null,
                            card?.Id,
                            TouchPointer || board.Archived,
                            item && xInColumn < Handle && !board.Archived,
                            p.x,
                            p.y
                        );
                        _panColumn = hit?.Id;
                        _last = p;
                        _edgeTime = Time.unscaledTime;
                        retarget = true;
                    }
                    e.Use();
                }
                if (retarget && _gesture.Dragging)
                    Target(board, hit, index, xInColumn, width, area.height, p.y);
                if (_gesture.Held && e.rawType == EventType.MouseUp && e.button == 0)
                {
                    if (_gesture.Dragging && p != _last)
                        Target(board, hit, index, xInColumn, width, area.height, p.y);
                    if (
                        !_gesture.Panning
                        && !_gesture.Dragging
                        && card != null
                        && card.Id == _gesture.Card
                    )
                        _editor.Select(card);
                    var edit = _gesture.Release(
                        board,
                        hit != null && (_gesture.Card == null || !header)
                    );
                    if (edit != null)
                        Save(edit);
                    e.Use();
                }
                if (e.type != EventType.Repaint)
                    return;
                for (int i = 0; i < board.Columns.Count; i++)
                {
                    float x = i * (width + Gap) - _viewport.Left;
                    if (x >= area.width || x + width <= 0)
                        continue;
                    PaintColumn(board, board.Columns[i], x, width, area.height);
                }
            }
            finally
            {
                GUI.EndGroup();
            }
        }

        static float Edge(float coordinate, float extent) =>
            coordinate < 28 ? -1
            : coordinate > extent - 28 ? 1
            : 0;

        void Target(
            Wire.ProjectBoard board,
            Wire.BoardColumn hit,
            int index,
            float x,
            float width,
            float height,
            float y
        )
        {
            if (hit == null || (_gesture.Card != null && y < Header))
            {
                _gesture.Target(null, null);
                return;
            }
            if (_gesture.Card == null)
            {
                int insertion = index + (x > width / 2 ? 1 : 0);
                _gesture.Target(
                    hit.Id,
                    insertion < board.Columns.Count ? board.Columns[insertion].Id : null
                );
            }
            else
            {
                int insertion = _viewport.Insertion(hit, height, y);
                _gesture.Target(
                    hit.Id,
                    insertion < hit.Cards.Count ? hit.Cards[insertion].Id : null
                );
            }
        }

        void PaintColumn(
            Wire.ProjectBoard board,
            Wire.BoardColumn column,
            float x,
            float width,
            float height
        )
        {
            Slab.Fill(new Rect(x, 0, width, height), UiTheme.Well);
            float top = _viewport.Top(column, height);
            int first = Mathf.FloorToInt(top / Row);
            for (int i = first; i < column.Cards.Count; i++)
            {
                float y = Header + i * Row - top;
                if (y >= height)
                    break;
                var card = column.Cards[i];
                Slab.Box(new Rect(x + 4, y + 2, width - 8, Row - 5), UiTheme.Panel, UiTheme.Edge);
                UiText.RowLabel(new Rect(x + 6, y + 4, Handle - 8, Row - 12), "≡");
                UiText.RowLabel(new Rect(x + Handle, y + 2, width - Handle - 8, 30), card.Title);
                UiText.RowLabel(
                    new Rect(x + Handle, y + 30, width - Handle - 8, 24),
                    card.Assignee
                );
            }
            Slab.Fill(new Rect(x, 0, width, Header), UiTheme.Panel);
            UiText.RowLabel(new Rect(x + 7, 0, Handle - 8, Header), "≡");
            UiText.RowLabel(new Rect(x + Handle, 0, width - Handle - 41, Header), column.Name);
            if (!board.Archived)
                UiText.RowLabel(
                    new Rect(x + width - 39, 0, 38, Header),
                    "···",
                    TextAnchor.MiddleCenter
                );
            if (!_gesture.Dragging || _gesture.Destination == null)
                return;
            if (_gesture.Card == null)
            {
                if (
                    _gesture.Before == column.Id
                    || (_gesture.Before == null && column == board.Columns.Last())
                )
                    Slab.Fill(
                        new Rect(_gesture.Before == null ? x + width - 4 : x, 0, 4, height),
                        UiTheme.Accent
                    );
            }
            else if (column.Id == _gesture.Destination)
            {
                int insert =
                    _gesture.Before == null
                        ? column.Cards.Count
                        : column.Cards.ToList().FindIndex(c => c.Id == _gesture.Before);
                float y = Header + insert * Row - top;
                if (y >= Header && y < height)
                    Slab.Fill(new Rect(x + 4, y, width - 8, 4), UiTheme.Accent);
            }
        }

        void ColumnMenu(Wire.ProjectBoard board, Wire.BoardColumn column)
        {
            Wire.BoardEdit Edit(string action, string text = "") =>
                new Wire.BoardEdit
                {
                    Action = action,
                    Board = board.Id,
                    Column = column.Id,
                    Text = text,
                };
            UiMenu.Open(
                new List<FloatMenuOption>
                {
                    new FloatMenuOption(
                        "Add card…",
                        () => Prompt("Card title", "", s => Save(Edit("add-card", s)))
                    ),
                    new FloatMenuOption(
                        "Rename column…",
                        () =>
                            Prompt("Column name", column.Name, s => Save(Edit("rename-column", s)))
                    ),
                    new FloatMenuOption(
                        "Remove empty column",
                        column.Cards.Count == 0 ? () => Save(Edit("remove-column")) : null
                    ),
                }
            );
        }
    }
}
