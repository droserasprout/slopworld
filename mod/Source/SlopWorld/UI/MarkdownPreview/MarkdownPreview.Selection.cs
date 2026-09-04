using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Owns the selection model and its geometry. Mouse gesture routing remains in
    // MarkdownInputController, while rendering only asks this object for highlights.
    sealed class MarkdownSelection
    {
        readonly List<SelectionLine> _lines = new List<SelectionLine>();
        StyleSet _styles;
        bool _dragging;
        bool _selectionMoved;
        bool _multiClickSelection;
        bool _wordDragging;
        bool _lineDragging;
        bool _hasSelection;
        int _lineStart;
        int _selectionControl;
        Vector2Int _wordStart;
        Vector2Int _wordEnd;
        Vector2Int _selectionStart;
        Vector2Int _selectionEnd;

        public List<SelectionLine> Lines => _lines;
        public bool HasSelection => _hasSelection;

        public void AttachStyles(StyleSet styles)
        {
            _styles = styles;
        }

        public void Rebuild(List<Placement> placements)
        {
            _lines.Clear();
            foreach (var placement in placements)
            {
                switch (placement.Kind)
                {
                    case PlacementKind.Text:
                    case PlacementKind.Bullet:
                        CollectText(placement.Text, placement.X, placement.Y);
                        break;

                    case PlacementKind.Code:
                        CollectText(placement.Text, placement.X + SlopWidgets.GapS,
                            placement.Y + SlopWidgets.GapS +
                            (string.IsNullOrWhiteSpace(placement.Label)
                                ? 0f : SlopWidgets.TinyH + SlopWidgets.GapXS));
                        break;

                    case PlacementKind.Table:
                        CollectTable(placement);
                        break;
                }
            }
        }

        public void Clear()
        {
            _hasSelection = false;
            _dragging = false;
            _selectionMoved = false;
            _multiClickSelection = false;
            _wordDragging = false;
            _lineDragging = false;
            ReleaseSelection();
        }

        public void BeginMouse(Rect body, Event e, int clickCount, Vector2 scroll)
        {
            if (!body.Contains(e.mousePosition)) return;
            var point = PointAt(body, e.mousePosition, scroll);
            if (clickCount >= 3)
            {
                CaptureSelection(body);
                SelectLine(point.y);
                e.Use();
                return;
            }
            if (clickCount >= 2)
            {
                CaptureSelection(body);
                DoubleClickSelect(point);
                e.Use();
                return;
            }

            bool extend = e.shift && _hasSelection;
            _selectionStart = extend ? _selectionStart : point;
            _selectionEnd = point;
            _dragging = true;
            _selectionMoved = false;
            _multiClickSelection = false;
            _wordDragging = false;
            _lineDragging = false;
            _hasSelection = extend && _selectionStart != _selectionEnd;
            CaptureSelection(body);
            e.Use();
        }

        public void DragMouse(Rect body, Event e, Vector2 scroll)
        {
            if (!_dragging) return;
            _selectionMoved = true;
            var drag = PointAt(body, e.mousePosition, scroll);
            if (_lineDragging) SelectLineRange(_lineStart, drag.y);
            else if (_wordDragging) UpdateWordSelection(drag);
            else
            {
                _selectionEnd = drag;
                _hasSelection = _selectionStart != _selectionEnd;
            }
            e.Use();
        }

        public void EndMouse(Rect body, Event e, Vector2 scroll)
        {
            if (!_dragging) return;
            var up = PointAt(body, e.mousePosition, scroll);
            // A double click selects a word and a triple click replaces it with a line. Do not
            // copy the intermediate word; multi-click selection is visual until Ctrl+C or the
            // Copy menu is used, otherwise one gesture starts multiple wl-copy owners.
            bool copy = !_multiClickSelection || _selectionMoved;
            if (_lineDragging)
            {
                SelectLineRange(_lineStart, up.y);
                _lineDragging = false;
                _dragging = false;
                ReleaseSelection();
                if (copy) CopySelection();
            }
            else if (_wordDragging)
            {
                UpdateWordSelection(up);
                _wordDragging = false;
                _dragging = false;
                ReleaseSelection();
                if (_hasSelection && copy) CopySelection();
            }
            else
            {
                _dragging = false;
                _selectionEnd = up;
                if (_selectionStart != _selectionEnd)
                {
                    _hasSelection = true;
                    if (copy) CopySelection();
                }
                else _hasSelection = false;
                ReleaseSelection();
            }
            _selectionMoved = false;
            _multiClickSelection = false;
            e.Use();
        }

        public void HandleKey(Event e)
        {
            if (!e.control || e.alt) return;

            if (e.keyCode == KeyCode.C)
            {
                if (_hasSelection) CopySelection();
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.A)
            {
                SelectAll();
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.V)
            {
                TerminalWindow.PasteClipboardToAgent();
                e.Use();
            }
        }

        public void OpenMenu()
        {
            var options = new List<FloatMenuOption>();
            var copy = new FloatMenuOption("Copy", CopySelection);
            copy.Disabled = !_hasSelection;
            options.Add(copy);

            var paste = new FloatMenuOption("Paste", TerminalWindow.PasteClipboardToAgent);
            paste.Disabled = !TerminalWindow.CanPasteClipboardToAgent;
            options.Add(paste);
            options.Add(new FloatMenuOption("Select all", SelectAll));
            TerminalWindow.OpenOverPane(new SlopMenu(options));
        }

        public void DrawHighlights(float clipTop, float clipBottom)
        {
            if (!_hasSelection || _lines.Count == 0) return;
            OrderedSelection(out var a, out var b);

            int first = Mathf.Clamp(a.y, 0, _lines.Count - 1);
            int last = Mathf.Clamp(b.y, 0, _lines.Count - 1);
            for (int i = first; i <= last; i++)
            {
                var line = _lines[i];
                if (line.Y + line.Height <= clipTop || line.Y >= clipBottom) continue;
                EnsureEdges(line);
                int start = i == a.y ? a.x : 0;
                int end = i == b.y ? b.x : line.Text.Length;
                start = Mathf.Clamp(start, 0, line.Text.Length);
                end = Mathf.Clamp(end, start, line.Text.Length);
                if (end <= start) continue;

                float left = line.X + line.Edges[start];
                float right = line.X + line.Edges[end];
                Slab.Fill(new Rect(left, line.Y, right - left, line.Height), SlopWidgets.Sel);
            }
        }

        public void EnsureEdges(SelectionLine line)
        {
            if (line == null || line.Edges.Count == line.Text.Length + 1) return;

            line.Edges.Clear();
            line.Edges.Add(0f);
            float at = 0f;
            foreach (var piece in line.Source.Pieces)
            {
                if (piece.Run.IsImage)
                {
                    at += piece.Width;
                    continue;
                }
                if (piece.Run.IsTask)
                {
                    float markerWidth = 0f;
                    for (int i = 0; i < piece.Text.Length; i++)
                        markerWidth += _styles.MeasureChar(piece.Style, piece.Text[i]);
                    float scale = markerWidth <= 0f ? 0f : piece.Width / markerWidth;
                    for (int i = 0; i < piece.Text.Length; i++)
                    {
                        at += _styles.MeasureChar(piece.Style, piece.Text[i]) * scale;
                        line.Edges.Add(at);
                    }
                    continue;
                }
                for (int i = 0; i < piece.Text.Length; i++)
                {
                    at += _styles.MeasureChar(piece.Style, piece.Text[i]);
                    line.Edges.Add(at);
                }
            }
        }

        Vector2Int PointAt(Rect body, Vector2 mouse, Vector2 scroll)
        {
            if (_lines.Count == 0) return Vector2Int.zero;

            float y = mouse.y - body.y + scroll.y;
            float x = mouse.x - body.x + scroll.x;
            int low = 0;
            int high = _lines.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_lines[middle].Y < y) low = middle + 1;
                else high = middle;
            }

            int first = Mathf.Max(0, low - 1);
            int last = Mathf.Min(_lines.Count - 1, low);
            if (first < _lines.Count)
            {
                while (first > 0 && Mathf.Approximately(_lines[first - 1].Y, _lines[first].Y))
                    first--;
                while (last + 1 < _lines.Count && Mathf.Approximately(_lines[last + 1].Y,
                    _lines[last].Y)) last++;
            }

            int lineIndex = first;
            float best = float.MaxValue;
            for (int i = first; i <= last; i++)
            {
                var line = _lines[i];
                float vertical = y < line.Y ? line.Y - y :
                    y > line.Y + line.Height ? y - (line.Y + line.Height) : 0f;
                float left = line.X;
                float right = line.X + line.Width;
                float horizontal = x < left ? left - x : x > right ? x - right : 0f;
                float distance = vertical * 10000f + horizontal;
                if (distance < best)
                {
                    best = distance;
                    lineIndex = i;
                }
            }

            var selected = _lines[lineIndex];
            EnsureEdges(selected);
            if (x <= selected.X) return new Vector2Int(0, lineIndex);
            if (x >= selected.X + selected.Edges[selected.Text.Length])
                return new Vector2Int(selected.Text.Length, lineIndex);

            for (int i = 0; i < selected.Text.Length; i++)
            {
                float left = selected.X + selected.Edges[i];
                float right = selected.X + selected.Edges[i + 1];
                if (x < (left + right) * 0.5f)
                    return new Vector2Int(i, lineIndex);
            }
            return new Vector2Int(selected.Text.Length, lineIndex);
        }

        void DoubleClickSelect(Vector2Int point)
        {
            if (point.y < 0 || point.y >= _lines.Count) return;
            var line = _lines[point.y];
            if (line.Text.Length == 0) { Clear(); return; }

            int index = Mathf.Clamp(point.x, 0, line.Text.Length - 1);
            char anchor = line.Text[index];
            bool word = IsWordChar(anchor);
            int start = index;
            int end = index + 1;
            while (start > 0 && SameClass(line.Text[start - 1], anchor, word)) start--;
            while (end < line.Text.Length && SameClass(line.Text[end], anchor, word)) end++;

            _wordStart = new Vector2Int(start, point.y);
            _wordEnd = new Vector2Int(end, point.y);
            _selectionStart = _wordStart;
            _selectionEnd = _wordEnd;
            _hasSelection = true;
            _dragging = true;
            _selectionMoved = false;
            _multiClickSelection = true;
            _wordDragging = true;
            _lineDragging = false;
        }

        void UpdateWordSelection(Vector2Int point)
        {
            if (_lines.Count == 0) return;
            int lineIndex = Mathf.Clamp(point.y, 0, _lines.Count - 1);
            var line = _lines[lineIndex];
            if (line.Text.Length == 0) return;

            int index = Mathf.Clamp(point.x, 0, line.Text.Length - 1);
            char anchor = line.Text[index];
            bool word = IsWordChar(anchor);
            int start = index;
            int end = index + 1;
            while (start > 0 && SameClass(line.Text[start - 1], anchor, word)) start--;
            while (end < line.Text.Length && SameClass(line.Text[end], anchor, word)) end++;

            var destinationStart = new Vector2Int(start, lineIndex);
            var destinationEnd = new Vector2Int(end, lineIndex);
            if (Before(point, _wordStart))
            {
                _selectionStart = destinationStart;
                _selectionEnd = _wordEnd;
            }
            else
            {
                _selectionStart = _wordStart;
                _selectionEnd = destinationEnd;
            }
            _hasSelection = _selectionStart != _selectionEnd;
        }

        void SelectLine(int line)
        {
            if (line < 0 || line >= _lines.Count) return;
            int length = _lines[line].Text.Length;
            if (length == 0) { Clear(); return; }
            _lineStart = line;
            _selectionStart = new Vector2Int(0, line);
            _selectionEnd = new Vector2Int(length, line);
            _hasSelection = true;
            _dragging = true;
            _selectionMoved = false;
            _multiClickSelection = true;
            _wordDragging = false;
            _lineDragging = true;
        }

        void SelectLineRange(int anchor, int line)
        {
            if (_lines.Count == 0) return;
            anchor = Mathf.Clamp(anchor, 0, _lines.Count - 1);
            line = Mathf.Clamp(line, 0, _lines.Count - 1);
            int first = Mathf.Min(anchor, line);
            int last = Mathf.Max(anchor, line);
            _selectionStart = new Vector2Int(0, first);
            _selectionEnd = new Vector2Int(_lines[last].Text.Length, last);
            _hasSelection = true;
        }

        static bool Before(Vector2Int a, Vector2Int b) =>
            a.y < b.y || (a.y == b.y && a.x < b.x);

        static bool SameClass(char c, char anchor, bool word) =>
            word ? IsWordChar(c) : c == anchor;

        static bool IsWordChar(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
            (c >= '0' && c <= '9') || c == '_';

        void SelectAll()
        {
            if (_lines.Count == 0) return;
            _selectionStart = Vector2Int.zero;
            int last = _lines.Count - 1;
            _selectionEnd = new Vector2Int(_lines[last].Text.Length, last);
            _hasSelection = true;
            _dragging = false;
            _selectionMoved = false;
            _multiClickSelection = false;
            _wordDragging = false;
            _lineDragging = false;
            ReleaseSelection();
            CopyText(SelectionText().TrimEnd('\n'));
        }

        void CopySelection()
        {
            if (_hasSelection) CopyText(SelectionText());
        }

        string SelectionText()
        {
            if (_lines.Count == 0 || !_hasSelection) return "";
            OrderedSelection(out var a, out var b);
            int first = Mathf.Clamp(a.y, 0, _lines.Count - 1);
            int last = Mathf.Clamp(b.y, 0, _lines.Count - 1);
            var output = new StringBuilder();
            for (int i = first; i <= last; i++)
            {
                string text = _lines[i].Text;
                int start = i == a.y ? a.x : 0;
                int end = i == b.y ? b.x : text.Length;
                start = Mathf.Clamp(start, 0, text.Length);
                end = Mathf.Clamp(end, start, text.Length);
                if (end > start) output.Append(text.Substring(start, end - start));
                if (i < last) output.Append('\n');
            }
            return output.ToString();
        }

        void OrderedSelection(out Vector2Int a, out Vector2Int b)
        {
            a = _selectionStart;
            b = _selectionEnd;
            if (Before(b, a))
            {
                var temp = a;
                a = b;
                b = temp;
            }
        }

        static void CollectText(List<SelectionLine> target, TextLayout text, float x, float y)
        {
            if (text == null) return;
            foreach (var line in text.Lines)
                CollectTextLine(target, line, x, y);
        }

        void CollectText(TextLayout text, float x, float y) => CollectText(_lines, text, x, y);

        static void CollectTextLine(List<SelectionLine> target, TextLine line, float x, float y)
        {
            var output = new SelectionLine
            {
                X = x,
                Y = y + line.Offset,
                Height = line.Height,
                Width = line.Width,
                Text = "",
                Source = line,
            };
            var chars = new StringBuilder();
            foreach (var piece in line.Pieces)
            {
                if (piece.Run.IsImage) continue;
                chars.Append(piece.Text);
            }
            output.Text = chars.ToString();
            target.Add(output);
        }

        void CollectTable(Placement placement)
        {
            foreach (var row in placement.Table.Rows)
            {
                int lines = 0;
                foreach (var cell in row.Cells) lines = Mathf.Max(lines, cell.Lines.Count);
                for (int lineIndex = 0; lineIndex < lines; lineIndex++)
                {
                    float x = placement.X;
                    for (int i = 0; i < row.Cells.Count; i++)
                    {
                        var cell = row.Cells[i];
                        if (lineIndex < cell.Lines.Count)
                            CollectTextLine(_lines, cell.Lines[lineIndex], x + SlopWidgets.GapS,
                                placement.Y + row.Offset + SlopWidgets.GapS);
                        x += placement.Table.Widths[i];
                    }
                }
            }
        }

        void CaptureSelection(Rect body)
        {
            if (_selectionControl != 0 && GUIUtility.hotControl == _selectionControl)
                GUIUtility.hotControl = 0;
            _selectionControl = GUIUtility.GetControlID(FocusType.Passive, body);
            GUIUtility.hotControl = _selectionControl;
        }

        void ReleaseSelection()
        {
            if (_selectionControl != 0 && GUIUtility.hotControl == _selectionControl)
                GUIUtility.hotControl = 0;
            _selectionControl = 0;
        }

        static void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            SlopClipboard.Copy(text, null,
                msg => Log.Warning($"[SlopWorld] clipboard: {msg}"));
        }
    }
}
