using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared document gestures, capture and highlights. Adapters own line geometry,
    // copy semantics and clipboard policy; terminal cell selection is a separate owner.
    abstract class DocumentSelection<TLine> where TLine : DocumentSelectionLine
    {
        protected readonly List<TLine> _lines = new List<TLine>();
        readonly List<TLine> _spatialLines = new List<TLine>();
        bool _dragging;
        bool _wordDragging;
        bool _lineDragging;
        bool _hasSelection;
        int _lineStart;
        int _selectionControl;
        Vector2Int _wordStart;
        Vector2Int _wordEnd;
        protected Vector2Int _selectionStart;
        protected Vector2Int _selectionEnd;

        public List<TLine> Lines => _lines;
        public bool HasSelection => _hasSelection;

        protected virtual bool PublishPrimary => true;
        protected virtual bool CopyOnSelectAll => true;
        protected virtual bool CanPaste => true;
        protected virtual bool ExtendRequiresSelection => true;
        protected virtual float MinimumHighlightWidth => 0f;
        protected abstract void EnsureEdges(TLine line);
        protected abstract string CopyRange(Vector2Int start, Vector2Int end);

        protected void Reindex()
        {
            _spatialLines.Clear();
            _spatialLines.AddRange(_lines);
            _spatialLines.Sort((left, right) =>
            {
                int result = left.Y.CompareTo(right.Y);
                if (result != 0) return result;
                result = left.X.CompareTo(right.X);
                return result != 0 ? result : left.LogicalIndex.CompareTo(right.LogicalIndex);
            });
        }

        protected void RestoreRange(Vector2Int start, Vector2Int end)
        {
            _selectionStart = start;
            _selectionEnd = end;
            _hasSelection = start != end;
        }

        public void Clear()
        {
            _selectionStart = _selectionEnd = Vector2Int.zero;
            _hasSelection = false;
            _dragging = false;
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

            bool extend = e.shift && (!ExtendRequiresSelection || _hasSelection);
            _selectionStart = extend ? _selectionStart : point;
            _selectionEnd = point;
            _dragging = true;
            _wordDragging = false;
            _lineDragging = false;
            _hasSelection = extend && _selectionStart != _selectionEnd;
            CaptureSelection(body);
            e.Use();
        }

        public void DragMouse(Rect body, Event e, Vector2 scroll)
        {
            if (!_dragging) return;
            UpdateDrag(PointAt(body, e.mousePosition, scroll));
            e.Use();
        }

        void UpdateDrag(Vector2Int point)
        {
            if (_lineDragging) SelectLineRange(_lineStart, point.y);
            else if (_wordDragging) UpdateWordSelection(point);
            else RestoreRange(_selectionStart, point);
        }

        public void EndMouse(Rect body, Event e, Vector2 scroll)
        {
            if (!_dragging) return;
            UpdateDrag(PointAt(body, e.mousePosition, scroll));
            _dragging = _lineDragging = _wordDragging = false;
            ReleaseSelection();
            // Readers choose whether mouse selection publishes PRIMARY. CLIPBOARD remains explicit.
            CopyPrimarySelection();
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

            if (CanPaste && e.keyCode == KeyCode.V)
            {
                TerminalWindow.PasteClipboardToAgent();
                e.Use();
            }
        }

        public void OpenMenu()
        {
            var options = new List<FloatMenuOption>();
            SelectionCommands.Add(options,
                new SelectionCommandAvailability(canCopy: _hasSelection, canPaste: CanPaste && TerminalWindow.CanPasteClipboardToAgent, canSelectAll: true, canCut: false),
                CopySelection, CanPaste ? TerminalWindow.PasteClipboardToAgent : (System.Action)null, SelectAll);
            TerminalWindow.OpenOverPane(new UiMenu(options));
        }

        protected virtual int FirstHighlightLine(int selected, float top) => selected;

        public void DrawHighlights(float clipTop, float clipBottom)
        {
            if (!_hasSelection || _lines.Count == 0) return;
            OrderedSelection(out var a, out var b);

            int first = FirstHighlightLine(Mathf.Clamp(a.y, 0, _lines.Count - 1), clipTop);
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

                float left = line.X + Mathf.Min(line.Width, line.Edges[start]);
                float right = line.X + Mathf.Min(line.Width, line.Edges[end]);
                Slab.Fill(new Rect(left, line.Y, Mathf.Max(MinimumHighlightWidth, right - left), line.Height), UiTheme.Sel);
            }
        }

        Vector2Int PointAt(Rect body, Vector2 mouse, Vector2 scroll)
        {
            if (_lines.Count == 0) return Vector2Int.zero;

            float y = mouse.y - body.y + scroll.y;
            float x = mouse.x - body.x + scroll.x;
            int lineIndex = NearestLine(x, y);

            var selected = _lines[lineIndex];
            EnsureEdges(selected);
            if (x <= selected.X) return new Vector2Int(0, lineIndex);
            if (x >= selected.X + selected.Edges[selected.Text.Length])
                return new Vector2Int(selected.Text.Length, lineIndex);

            int[] boundaries = selected.Boundaries;
            int count = boundaries == null ? selected.Text.Length : boundaries.Length - 1;
            for (int i = 0; i < count; i++)
            {
                int start = boundaries == null ? i : boundaries[i];
                int end = boundaries == null ? i + 1 : boundaries[i + 1];
                float left = selected.X + selected.Edges[start];
                float right = selected.X + selected.Edges[end];
                if (x < (left + right) * 0.5f)
                    return new Vector2Int(start, lineIndex);
            }
            return new Vector2Int(selected.Text.Length, lineIndex);
        }

        protected virtual int NearestLine(float x, float y)
        {
            int lineIndex = _spatialLines[0].LogicalIndex;
            float best = float.MaxValue;
            foreach (var line in _spatialLines)
            {
                float vertical = y < line.Y ? line.Y - y :
                    y > line.Y + line.Height ? y - (line.Y + line.Height) : 0f;
                float left = line.X;
                float right = line.X + line.Width;
                float horizontal = x < left ? left - x : x > right ? x - right : 0f;
                float distance = vertical * 10000f + horizontal;
                if (distance < best)
                {
                    best = distance;
                    lineIndex = line.LogicalIndex;
                }
            }

            return lineIndex;
        }

        void DoubleClickSelect(Vector2Int point)
        {
            if (point.y < 0 || point.y >= _lines.Count) return;
            var line = _lines[point.y];
            if (line.Text.Length == 0) { Clear(); return; }

            int index = Mathf.Clamp(point.x, 0, line.Text.Length - 1);
            var range = TextSelectionRules.WordRange(line.Text, index);
            int start = range.Start;
            int end = range.End;

            _wordStart = new Vector2Int(start, point.y);
            _wordEnd = new Vector2Int(end, point.y);
            _selectionStart = _wordStart;
            _selectionEnd = _wordEnd;
            _hasSelection = true;
            _dragging = true;
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
            var range = TextSelectionRules.WordRange(line.Text, index);
            int start = range.Start;
            int end = range.End;

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

        public void SelectAll()
        {
            if (_lines.Count == 0) return;
            _selectionStart = Vector2Int.zero;
            int last = _lines.Count - 1;
            _selectionEnd = new Vector2Int(_lines[last].Text.Length, last);
            _hasSelection = true;
            _dragging = false;
            _wordDragging = false;
            _lineDragging = false;
            ReleaseSelection();
            if (CopyOnSelectAll) CopyText(SelectionText().TrimEnd('\n'));
        }

        void CopySelection()
        {
            if (_hasSelection) CopyText(SelectionText());
        }

        void CopyPrimarySelection()
        {
            if (PublishPrimary && _hasSelection) CopyPrimaryText(SelectionText());
        }

        public string SelectionText()
        {
            if (_lines.Count == 0 || !_hasSelection) return "";
            OrderedSelection(out var a, out var b);
            return CopyRange(a, b);
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
            DaemonClipboard.Copy(text, null,
                msg => Log.Warning($"[SlopWorld] clipboard: {msg}"));
        }

        static void CopyPrimaryText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            DaemonClipboard.CopyPrimary(text, null,
                msg => Log.Warning($"[SlopWorld] primary selection: {msg}"));
        }
    }
}
